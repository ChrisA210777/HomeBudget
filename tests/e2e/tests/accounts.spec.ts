import { expect, test } from './fixtures';
import { addIncome, choose, openFromNav, settled, signIn, typeOutgoingName } from './helpers';

// BUD-89: accounts and pots, chosen as "Paid from" on outgoings.

test('accounts and pots are added from the main navigation', async ({ page }, testInfo) => {
  await signIn(page);

  await openFromNav(page, testInfo, 'Accounts');
  await expect(page.getByRole('heading', { level: 1, name: 'Accounts' })).toBeVisible();
  await settled(page);

  await page.getByTestId('new-account-name').fill('Joint Monzo');
  await page.getByTestId('new-account-provider').fill('Monzo');
  await page.getByTestId('add-account').click();
  await expect(page.getByTestId('account-name')).toHaveText(['Joint Monzo']);

  await page.getByTestId('add-pot').click();
  await page.getByTestId('new-pot-name').fill('Holiday');
  await page.getByTestId('add-pot-save').click();
  await expect(page.getByTestId('account-name')).toHaveText(['Joint Monzo', 'Holiday']);

  await expect(page.getByTestId('account-details').first()).toContainText('Monzo');
});

test('income can be paid into an account, and grouped by it', async ({ page }) => {
  await signIn(page);
  await page.goto('/income/new');
  await settled(page);

  // BUD-90: typing a new account name adds it.
  await page.getByTestId('name').fill('Child Benefit');
  await page.getByTestId('amount').fill('110');
  await page.getByTestId('paid-into').fill('Joint Monzo (Bills Pot)');
  await page.getByTestId('paid-into').press('Tab');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('income-dates')).toContainText('Paid into Joint Monzo › Bills');

  // BUD-99: always grouped by account, with a pot counted in its account; headings once there's more than one.
  await page.goto('/income');
  await settled(page);
  await expect(page.getByTestId('paid-into-group')).toHaveCount(0);
  await expect(page.getByTestId('income-person')).toContainText('Bills pot');

  await addIncome(page, 'Salary', '2500');
  await page.goto('/income');
  await settled(page);
  await expect(page.getByTestId('paid-into-name')).toHaveText(['Joint Monzo', 'Not set']);
});

test("an account's outgoings are one tap away from the Accounts page", async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);
  await typeOutgoingName(page, 'Hedge trimming fund');
  await choose(page, 'category', 'Home maintenance');
  await choose(page, 'priority', 'Optional');
  await page.getByTestId('amount').fill('20');
  await page.getByTestId('toggle-details').click();
  await page.getByTestId('paid-from').fill('Garden account');
  await page.getByTestId('paid-from').press('Tab');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('outgoing-name').filter({ hasText: 'Hedge trimming fund' })).toBeVisible();

  // BUD-95: the count links to Outgoings, filtered to that account.
  await page.goto('/accounts');
  await settled(page);
  await page.getByTestId('account-outgoings').click();
  await expect(page).toHaveURL(/\/outgoings\?account=/);
  await settled(page);
  await expect(page.getByTestId('outgoing-name')).toHaveText(['Hedge trimming fund']);
  await expect(page.getByTestId('month-total')).toHaveText('£20.00');

  // BUD-96, BUD-58: Upcoming payments filters by account.
  await page.goto('/outgoings/upcoming');
  await settled(page);
  await choose(page, 'filter-account', 'Garden account');
  await expect(page.getByTestId('payment-name')).toHaveText(['Hedge trimming fund']);
  await expect(page.getByTestId('period-total')).toHaveText('£20.00');
});

test('grouped by Paid from, an account shows its pots as subgroups with subtotals', async ({ page }) => {
  await signIn(page);
  for (const [name, paidFrom, amount] of [['Gnome repairs', 'Garden account', '15'], ['Hedge trimming fund', 'Garden account (Hedges Pot)', '20']]) {
    await page.goto('/outgoings/new');
    await settled(page);
    await typeOutgoingName(page, name);
    await choose(page, 'category', 'Home maintenance');
    await choose(page, 'priority', 'Optional');
    await page.getByTestId('amount').fill(amount);
    await page.getByTestId('toggle-details').click();
    await page.getByTestId('paid-from').fill(paidFrom);
    await page.getByTestId('paid-from').press('Tab');
    await page.getByTestId('save').click();
    await expect(page.getByTestId('outgoing-name').filter({ hasText: name })).toBeVisible();
  }

  await page.goto('/outgoings');
  await settled(page);
  await page.getByTestId('group-paid-from').click();

  // BUD-101: the account's total, then what's not in a pot and each pot.
  await expect(page.getByTestId('paid-from-name')).toHaveText(['Garden account']);
  await expect(page.getByTestId('category-subtotal')).toHaveText(['£35.00']);
  await expect(page.getByTestId('pot-name')).toHaveText(['Not in a pot', 'Hedges pot']);
  await expect(page.getByTestId('pot-subtotal')).toHaveText(['£15.00', '£20.00']);
});
