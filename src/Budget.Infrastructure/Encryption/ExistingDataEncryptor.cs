using System.Data.Common;
using Budget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Budget.Infrastructure.Encryption;

/// <summary>
/// Encrypts values stored before BUD-166, and fills in missing name keys, in place. It reads and writes the columns
/// directly, so audit fields ("changed by", "changed at") and recent activity aren't touched. Safe to run again: only
/// rows with a value not yet encrypted, or no name key, are changed.
/// </summary>
/// <remarks>
/// The SQL is built from the EF model's own table and column names (never from input), and every value is a
/// parameter.
/// </remarks>
public static class ExistingDataEncryptor
{
    /// <summary>Encrypts what's left to encrypt. Returns how many rows were changed.</summary>
    public static async Task<int> RunAsync(BudgetDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var changed = 0;
        foreach (var entityType in db.Model.GetEntityTypes().Where(e => e.BaseType is null && e.GetTableName() is not null))
        {
            changed += await RunAsync(db, entityType, cancellationToken);
        }

        return changed;
    }

    private static async Task<int> RunAsync(BudgetDbContext db, IEntityType entityType, CancellationToken cancellationToken)
    {
        var encrypted = entityType.GetProperties().Where(p => p.IsEncrypted()).ToList();
        var nameSource = entityType.NameKeySource() is { } source ? entityType.FindProperty(source) : null;
        if (encrypted.Count == 0 && nameSource is null)
        {
            return 0;
        }

        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        // Some tables have a key of more than one column (e.g. a household and a user).
        var keyColumns = entityType.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(table)!).ToList();
        var columns = encrypted.Select(p => p.GetColumnName(table)!).ToList();
        var nameColumn = nameSource?.GetColumnName(table);
        var nameKeyColumn = nameSource is null ? null : EncryptionModelExtensions.NameKeyColumn;

        var conditions = columns.Select(c => $"({Quote(c)} IS NOT NULL AND {Quote(c)} NOT LIKE '{FieldEncryption.Prefix}%')").ToList();
        if (nameKeyColumn is not null)
        {
            conditions.Add($"{Quote(nameKeyColumn)} IS NULL");
        }

        var selected = new List<string>(keyColumns);
        selected.AddRange(columns);
        if (nameColumn is not null && !columns.Contains(nameColumn))
        {
            selected.Add(nameColumn);
        }

        var tableName = (entityType.GetSchema() is { } schema ? Quote(schema) + "." : string.Empty) + Quote(table.Name);
        var rows = new List<Dictionary<string, object?>>();
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = $"SELECT {string.Join(", ", selected.Select(Quote))} FROM {tableName} WHERE {string.Join(" OR ", conditions)}";
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(selected.ToDictionary(c => c, c => reader[c] is DBNull ? null : reader[c]));
                }
            }

            foreach (var row in rows)
            {
                var set = new Dictionary<string, object?>();
                foreach (var column in columns)
                {
                    if (row[column] is string value && !FieldEncryption.IsEncrypted(value))
                    {
                        set[column] = FieldEncryption.Encrypt(value);
                    }
                }

                if (nameColumn is not null && nameKeyColumn is not null && row[nameColumn] is string name)
                {
                    set[nameKeyColumn] = FieldEncryption.NameKey(FieldEncryption.Decrypt(name));
                }

                if (set.Count == 0)
                {
                    continue;
                }

                await using var update = connection.CreateCommand();
                var assignments = new List<string>();
                var i = 0;
                foreach (var (column, value) in set)
                {
                    assignments.Add($"{Quote(column)} = @p{i}");
                    AddParameter(update, $"@p{i}", value);
                    i++;
                }

                var matches = new List<string>();
                for (var k = 0; k < keyColumns.Count; k++)
                {
                    matches.Add($"{Quote(keyColumns[k])} = @k{k}");
                    AddParameter(update, $"@k{k}", row[keyColumns[k]]);
                }

                update.CommandText = $"UPDATE {tableName} SET {string.Join(", ", assignments)} WHERE {string.Join(" AND ", matches)}";
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return rows.Count;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
