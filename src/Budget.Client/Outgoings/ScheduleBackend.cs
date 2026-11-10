using Budget.Client.Income;
using Budget.Shared.Outgoings;

namespace Budget.Client.Outgoings;

/// <summary>
/// What the amount history, pauses and ending show for something paid on a schedule: an outgoing or a
/// transfer (BUD-91). Both use the same rules (ADR-011, ADR-013) and the same history screens.
/// </summary>
public sealed record ScheduleView(
    string Name,
    int Every,
    PaymentUnitDto Unit,
    DateOnly? NextPayment,
    DateOnly? EndsOn,
    IReadOnlyList<DateOnly> UpcomingPayments,
    IReadOnlyList<OutgoingAmountEntry> AmountChanges,
    IReadOnlyList<OutgoingStatusEntry> StatusPeriods,
    IReadOnlyList<OutgoingCorrectionEntry> Corrections,
    string CurrencyCode,
    string Version);

/// <summary>Saves changes to one outgoing's or transfer's history, returning its details afterwards.</summary>
public interface IScheduleBackend<TDetails>
    where TDetails : class
{
    Task<TDetails?> GetAsync();

    Task DeleteAsync();

    Task<ApiResult<TDetails>> AddAmountChangeAsync(SaveOutgoingAmountRequest request);

    Task<ApiResult<TDetails>> CorrectAmountChangeAsync(Guid entryId, SaveOutgoingAmountRequest request);

    Task<ApiResult<TDetails>> DeleteAmountChangeAsync(Guid entryId, DeleteOutgoingEntryRequest request);

    Task<ApiResult<TDetails>> AddStatusPeriodAsync(SaveOutgoingStatusRequest request);

    Task<ApiResult<TDetails>> CorrectStatusPeriodAsync(Guid entryId, SaveOutgoingStatusRequest request);

    Task<ApiResult<TDetails>> DeleteStatusPeriodAsync(Guid entryId, DeleteOutgoingEntryRequest request);
}

/// <summary>An outgoing's history, saved through <see cref="IOutgoingApi"/>.</summary>
public sealed class OutgoingScheduleBackend(IOutgoingApi api, Guid householdId, Guid outgoingId) : IScheduleBackend<OutgoingDetailsResponse>
{
    public static ScheduleView View(OutgoingDetailsResponse d) => new(
        d.Name, d.Every, d.Unit, d.NextPayment, d.EndsOn, d.UpcomingPayments, d.AmountChanges, d.StatusPeriods, d.Corrections, d.CurrencyCode, d.Version);

    public Task<OutgoingDetailsResponse?> GetAsync() => api.GetAsync(householdId, outgoingId);

    public Task DeleteAsync() => api.DeleteAsync(householdId, outgoingId);

    public Task<ApiResult<OutgoingDetailsResponse>> AddAmountChangeAsync(SaveOutgoingAmountRequest request) =>
        api.AddAmountChangeAsync(householdId, outgoingId, request);

    public Task<ApiResult<OutgoingDetailsResponse>> CorrectAmountChangeAsync(Guid entryId, SaveOutgoingAmountRequest request) =>
        api.CorrectAmountChangeAsync(householdId, outgoingId, entryId, request);

    public Task<ApiResult<OutgoingDetailsResponse>> DeleteAmountChangeAsync(Guid entryId, DeleteOutgoingEntryRequest request) =>
        api.DeleteAmountChangeAsync(householdId, outgoingId, entryId, request);

    public Task<ApiResult<OutgoingDetailsResponse>> AddStatusPeriodAsync(SaveOutgoingStatusRequest request) =>
        api.AddStatusPeriodAsync(householdId, outgoingId, request);

    public Task<ApiResult<OutgoingDetailsResponse>> CorrectStatusPeriodAsync(Guid entryId, SaveOutgoingStatusRequest request) =>
        api.CorrectStatusPeriodAsync(householdId, outgoingId, entryId, request);

    public Task<ApiResult<OutgoingDetailsResponse>> DeleteStatusPeriodAsync(Guid entryId, DeleteOutgoingEntryRequest request) =>
        api.DeleteStatusPeriodAsync(householdId, outgoingId, entryId, request);
}
