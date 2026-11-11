using MudBlazor;

namespace Budget.Client.Tests;

/// <summary>Chooses the length of period from the period navigator's name (BUD-135).</summary>
internal static class PeriodMenu
{
    public static void ChoosePeriod(this IRenderedFragment cut, IRenderedComponent<MudPopoverProvider> popovers, string nameTestId, string choiceTestId)
    {
        cut.Find($"[data-testid={nameTestId}] button").Click();
        popovers.WaitForElement($"[data-testid={choiceTestId}]").Click();

        // The menu closes once a choice is made, ready to open again.
        popovers.WaitForAssertion(() => Assert.Empty(popovers.FindAll($"[data-testid={choiceTestId}]")));
    }
}
