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
