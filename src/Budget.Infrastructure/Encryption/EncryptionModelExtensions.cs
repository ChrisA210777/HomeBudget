using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Budget.Infrastructure.Encryption;

/// <summary>Marks columns as encrypted, and adds name keys for uniqueness (BUD-166).</summary>
public static class EncryptionModelExtensions
{
    /// <summary>The shadow column holding a name's keyed hash (<see cref="FieldEncryption.NameKey"/>).</summary>
    public const string NameKeyColumn = "NameKey";

    private const string EncryptedAnnotation = "Budget:Encrypted";
    private const string NameKeySourceAnnotation = "Budget:NameKeySource";

    private static readonly ValueConverter<string, string> Converter =
        new(value => FieldEncryption.Encrypt(value), stored => FieldEncryption.Decrypt(stored));

    /// <summary>
    /// Stores this column encrypted. Its maximum length stays the plaintext's (set it before calling this); the column
    /// is widened to fit when the model is finished (<see cref="ApplyEncryption"/>). The database can't sort, search or
    /// compare an encrypted column: do that in memory, or compare name keys.
    /// </summary>
    public static PropertyBuilder<TProperty> IsEncrypted<TProperty>(this PropertyBuilder<TProperty> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.Metadata.ClrType != typeof(string))
        {
            throw new InvalidOperationException($"Only text can be encrypted, not {property.Metadata.Name}.");
        }

        return property.HasAnnotation(EncryptedAnnotation, true);
    }

    /// <inheritdoc cref="IsEncrypted{TProperty}(PropertyBuilder{TProperty})"/>
    public static PropertyBuilder IsEncrypted(this PropertyBuilder property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.Metadata.ClrType != typeof(string))
        {
            throw new InvalidOperationException($"Only text can be encrypted, not {property.Metadata.Name}.");
        }

        return property.HasAnnotation(EncryptedAnnotation, true);
    }

    /// <summary>
    /// Adds a <c>NameKey</c> column holding a keyed hash of <paramref name="name"/>, kept up to date as it's saved
    /// (<see cref="NameKeyInterceptor"/>), so names can be unique without the database reading them.
    /// </summary>
    public static EntityTypeBuilder<T> HasNameKey<T>(this EntityTypeBuilder<T> entity, Expression<Func<T, string>> name)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(name);
        var source = ((MemberExpression)name.Body).Member.Name;
        entity.Property<string?>(NameKeyColumn).HasMaxLength(FieldEncryption.NameKeyLength);
        entity.HasAnnotation(NameKeySourceAnnotation, source);
        return entity;
    }

    /// <summary>The property a name key is made from, if the entity has one.</summary>
    public static string? NameKeySource(this IReadOnlyEntityType entityType) =>
        entityType.FindAnnotation(NameKeySourceAnnotation)?.Value as string;

    /// <summary>Whether a property is stored encrypted.</summary>
    public static bool IsEncrypted(this IReadOnlyProperty property) =>
        property.FindAnnotation(EncryptedAnnotation)?.Value is true;

    /// <summary>Gives every encrypted property its converter and a column wide enough for the encrypted text.</summary>
    public static void ApplyEncryption(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.IsEncrypted()))
        {
            var maxLength = property.GetMaxLength()
                ?? throw new InvalidOperationException($"Set a maximum length on {property.DeclaringType.Name}.{property.Name} before encrypting it.");
            property.SetValueConverter(Converter);
            property.SetMaxLength(FieldEncryption.StoredLength(maxLength));
        }
    }
}
