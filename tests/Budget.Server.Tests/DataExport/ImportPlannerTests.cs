using System.Text.Json;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Server.Features.DataExport;
using Budget.Shared.DataExport;

namespace Budget.Server.Tests.DataExport;

/// <summary>Building a household's data from a file (BUD-67, BUD-63).</summary>
public class ImportPlannerTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();

    private static HouseholdExport Golden() =>
        JsonSerializer.Deserialize<HouseholdExport>(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v1", "full-history-and-audit.json")), ExportFormat.JsonOptions)!;

    [Fact]
    public void New_ids_are_made_and_every_reference_is_remapped()
    {
        var export = Golden();

        var (plan, problems) = ImportPlanner.Plan(export, HouseholdId);

        Assert.Empty(problems);
        Assert.NotNull(plan);
        var fileIds = export.People.Select(p => p.Id).Concat(export.Categories.Select(c => c.Id)).ToHashSet();
        Assert.All(plan.People, p => Assert.DoesNotContain(p.Id, fileIds));
        Assert.All(plan.Categories, c => Assert.DoesNotContain(c.Id, fileIds));

        var chris = plan.People.Single(p => p.DisplayName == "Chris");
        var salary = plan.Incomes.Single(i => i.Name == "Salary");
        Assert.Equal(chris.Id, salary.PersonId);
        Assert.Equal("chris@example.com", plan.LinkedEmails[chris]);

        var insurance = plan.Categories.Single(c => c.Name == "Insurance");
        var car = plan.Outgoings.Single(o => o.Name == "Car insurance");
        Assert.Equal(insurance.Id, car.CategoryId);
        Assert.Equal(chris.Id, car.PersonId);
        Assert.True(plan.Categories.Single(c => c.Name == "Hobbies").IsHidden);
        Assert.Equal("music", plan.Categories.Single(c => c.Name == "Hobbies").Icon);
        Assert.All(plan.Categories.Concat<object>(plan.People).Concat(plan.Incomes).Concat(plan.Outgoings),
            item => Assert.Equal(HouseholdId, (Guid)item.GetType().GetProperty("HouseholdId")!.GetValue(item)!));
    }

    [Fact]
    public void History_is_rebuilt_with_corrections_pointing_at_the_new_entries()
    {
        var (plan, _) = ImportPlanner.Plan(Golden(), HouseholdId);

        var salary = plan!.Incomes.Single(i => i.Name == "Salary");
        Assert.Equal(new[] { 2800m, 2950m }, salary.AmountChanges.OrderBy(c => c.EffectiveFrom).Select(c => c.Amount));
        Assert.Equal(IncomeStatusReason.UnpaidLeave, salary.StatusPeriods.Single().Reason);
        var correction = salary.Corrections.Single();
        Assert.Equal(salary.AmountChanges.Single(c => c.Amount == 2950m).Id, correction.EntryId);
        Assert.Equal("Typed the wrong amount", correction.Reason);

        var mortgage = plan.Outgoings.Single(o => o.Name == "Mortgage");
        Assert.Equal(new DateOnly(2027, 2, 1), mortgage.StatusPeriods.Single().ResumesOn);
        Assert.Equal(OutgoingCorrectionAction.Deleted, mortgage.Corrections.Single().Action);
        Assert.Equal("Example Building Society", mortgage.Recipient);
        Assert.Equal(PaymentMethod.DirectDebit, mortgage.PaymentMethod);

        var guitar = plan.Outgoings.Single(o => o.Name == "Guitar lessons");
        Assert.Equal(new DateOnly(2026, 6, 1), guitar.EndsOn);

        var benefit = plan.Incomes.Single(i => i.Name == "Child Benefit").AmountChanges.Single();
        Assert.Equal((4, PaymentUnit.Week), (benefit.Every, benefit.Unit));
    }

    [Fact]
    public void Without_history_the_amount_in_force_is_used_from_the_first_payment()
    {
        var export = Golden();
        var mortgage = export.Outgoings[0] with
        {
            AmountChanges = [export.Outgoings[0].AmountChanges[1]], // only the 2027 amount
            StatusPeriods = [],
            Corrections = [],
        };

        var (plan, problems) = ImportPlanner.Plan(export with { Outgoings = [mortgage] }, HouseholdId);

        Assert.Empty(problems);
        var imported = plan!.Outgoings.Single();
        Assert.Equal(new DateOnly(2026, 1, 1), imported.FirstPaymentDate);
        Assert.Equal(new[] { (new DateOnly(2026, 1, 1), 1050m), (new DateOnly(2027, 3, 1), 1050m) },
            imported.AmountChanges.OrderBy(c => c.EffectiveFrom).Select(c => (c.EffectiveFrom, c.Amount)));
    }
}
