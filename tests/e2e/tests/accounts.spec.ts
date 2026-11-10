import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

// BUD-89: accounts and pots, chosen as "Paid from" on outgoings.

test('accounts and pots are added from the household menu', async ({ page }) => {
  await signIn(page);

  await page.getByTestId('appbar-household').getByRole('button').click();
  await page.getByTestId('household-accounts').click();
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

  await page.goto('/income');
  await settled(page);
  await page.getByTestId('group-paid-into').click();
  await expect(page.getByTestId('paid-into-name')).toHaveText(['Joint Monzo › Bills']);
});
