import { expect, test } from './fixtures';
import { choose, settled, signIn } from './helpers';

// BUD-92: once transfers are on, Cash flow shows each account's money in and out, monthly and day by day.

test('cash flow shows each account in and out, and day by day', async ({ page }) => {
  await signIn(page);
  await page.goto('/accounts');
  await settled(page);
  for (const name of ['Chris current', 'Joint bills']) {
    await page.getByTestId('new-account-name').fill(name);
    await page.getByTestId('add-account').click();
    await expect(page.getByTestId('account-name').filter({ hasText: name })).toBeVisible();
  }

  await page.getByTestId('transfers-setting').getByText('Track transfers between accounts').click();
  await page.getByTestId('go-to-transfers').click();
  await settled(page);
  await page.getByTestId('add-transfer').click();
  await settled(page);
  await choose(page, 'from-account', 'Chris current');
  await choose(page, 'to-account', 'Joint bills');
  await page.getByTestId('amount').fill('400');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('transfer-paid')).toHaveText('£400.00 a month');

  await page.goto('/transfers');
  await settled(page);
  await page.getByTestId('go-to-cash-flow').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Cash flow' })).toBeVisible();
  await settled(page);

  // The transfer leaves one account and arrives in the other.
  await expect(page.getByTestId('account-name')).toHaveText(['Chris current', 'Joint bills']);
  await expect(page.getByTestId('account-net')).toHaveText(['Short by £400.00', 'Surplus £400.00']);

  await page.getByTestId('show-daily').click();
  await settled(page);
  await expect(page.getByTestId('day-short').first()).toContainText('Short by £400.00');
  await expect(page.getByTestId('timing-gap')).toBeVisible();
});
