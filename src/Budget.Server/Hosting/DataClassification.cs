using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace Budget.Server.Hosting;

/// <summary>
/// The kinds of data that must never appear in logs (BUD-146). Log ids, not values; if a log message really must carry
/// one of these, mark its parameter with <see cref="PersonalDataAttribute"/> or <see cref="SecretDataAttribute"/> and
/// the value is erased before any logger sees it.
/// </summary>
public static class DataClassifications
{
    public const string TaxonomyName = "HomeBudget";

    /// <summary>Names, emails, amounts, and anything people type: notes, references, household and account names.</summary>
    public static DataClassification PersonalData => new(TaxonomyName, nameof(PersonalData));

    /// <summary>Invitation tokens, keys, and anything else that grants access.</summary>
    public static DataClassification SecretData => new(TaxonomyName, nameof(SecretData));

    /// <summary>Erases personal and secret values from logs. Use with <c>EnableRedaction()</c> on the logging builder.</summary>
    public static IServiceCollection AddBudgetRedaction(this IServiceCollection services) =>
        services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<ErasingRedactor>(new DataClassificationSet(PersonalData), new DataClassificationSet(SecretData));
            redaction.SetFallbackRedactor<ErasingRedactor>();
        });
}

/// <summary>A value that identifies or describes a person or their money: erased from logs (BUD-146).</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PersonalDataAttribute : DataClassificationAttribute
{
    public PersonalDataAttribute()
        : base(DataClassifications.PersonalData)
    {
    }
}

/// <summary>A value that grants access: erased from logs (BUD-146).</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SecretDataAttribute : DataClassificationAttribute
{
    public SecretDataAttribute()
        : base(DataClassifications.SecretData)
    {
    }
}
