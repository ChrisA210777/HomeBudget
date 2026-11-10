import { expect, test } from './fixtures';
import { choose, settled, signIn } from './helpers';

// BUD-91: an Admin turns on transfers, then records money moved between accounts, with history.

test('transfers between accounts are turned on, added and changed', async ({ page }) => {
  await signIn(page);
  await page.goto('/accounts');
  await settled(page);
  for (const name of ['Chris current', 'Joint bills']) {
    await page.getByTestId('new-account-name').fill(name);
    await page.getByTestId('add-account').click();
    await expect(page.getByTestId('account-name').filter({ hasText: name })).toBeVisible();
  }

  // Off until an Admin turns it on.
  await expect(page.getByTestId('go-to-transfers')).toHaveCount(0);
  await page.getByTestId('transfers-setting').getByText('Track transfers between accounts').click();
  await page.getByTestId('go-to-transfers').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Transfers' })).toBeVisible();
  await settled(page);
  await expect(page.getByTestId('transfers-empty')).toBeVisible();

  await page.getByTestId('add-transfer').click();
  await settled(page);
  await choose(page, 'from-account', 'Chris current');
  await choose(page, 'to-account', 'Joint bills');
  await page.getByTestId('amount').fill('400');
  await page.getByTestId('name').fill('Bills top-up');
  await page.getByTestId('save').click();

  await expect(page.getByTestId('transfer-name')).toHaveText('Bills top-up');
  await expect(page.getByTestId('transfer-paid')).toHaveText('£400.00 a month');
  await expect(page.getByTestId('to-account')).toHaveText('Joint bills');

  // The same history as an outgoing.
  await page.getByTestId('change-amount').click();
  await page.getByTestId('amount').fill('450');
  await page.getByTestId('comment').fill('Bills went up');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('amount-change')).toHaveCount(2);

  await page.goto('/transfers');
  await settled(page);
  await expect(page.getByTestId('transfer-group-name')).toHaveText(['Into Joint bills']);
  await expect(page.getByTestId('transfer-name')).toHaveText(['Bills top-up']);

});
