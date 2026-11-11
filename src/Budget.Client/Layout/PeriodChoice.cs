using Microsoft.AspNetCore.Components;

namespace Budget.Client.Layout;

/// <summary>A length of period offered by the <see cref="PeriodNavigator"/>, e.g. "A week" (BUD-135).</summary>
/// <param name="Name">What the menu shows.</param>
/// <param name="TestId">The menu item's data-testid.</param>
/// <param name="Selected">Whether this is the period shown.</param>
/// <param name="Choose">
/// Switches the page to this period. Create it on the page (<c>EventCallback.Factory.Create(this, ...)</c>) so the page,
/// not just the navigator, renders again afterwards.
/// </param>
public sealed record PeriodChoice(string Name, string TestId, bool Selected, EventCallback Choose);
