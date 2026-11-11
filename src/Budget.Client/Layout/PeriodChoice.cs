namespace Budget.Client.Layout;

/// <summary>A length of period offered by the <see cref="PeriodNavigator"/>, e.g. "A week" (BUD-135).</summary>
/// <param name="Name">What the menu shows.</param>
/// <param name="TestId">The menu item's data-testid.</param>
/// <param name="Selected">Whether this is the period shown.</param>
/// <param name="Choose">Switches the page to this period.</param>
public sealed record PeriodChoice(string Name, string TestId, bool Selected, Func<Task> Choose);
