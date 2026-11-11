import { expect, test } from './fixtures';
import { choose, choosePeriod, expectToolbarTidy, settled, signIn } from './helpers';

// BUD-92: once transfers are on, Cash flow shows each account's money in and out, monthly and day by day.

test('cash flow shows each account in and out, and day by day', async ({ page }) => {
  await signIn(page);
  await page.goto('/accounts');
  await settled(page);
  for (const name of ['Chris Austin everyday current account', 'Joint bills']) {
    await page.getByTestId('new-account-name').fill(name);
    await page.getByTestId('add-account').click();
    await expect(page.getByTestId('account-name').filter({ hasText: name })).toBeVisible();
  }

  await page.getByTestId('transfers-setting').getByText('Track transfers between accounts').click();
  await page.getByTestId('go-to-transfers').click();
  await settled(page);
  await page.getByTestId('add-transfer').click();
  await settled(page);
  await choose(page, 'from-account', 'Chris Austin everyday current account');
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
  await expect(page.getByTestId('account-name')).toHaveText(['Chris Austin everyday current account', 'Joint bills']);
  await expect(page.getByTestId('account-net')).toHaveText(['Short by £400.00', 'Surplus £400.00']);

  await page.getByTestId('show-daily').click();
  await settled(page);
  await expect(page.getByTestId('day-short').first()).toContainText('Short by £400.00');
  await expect(page.getByTestId('timing-gap')).toBeVisible();

  // The toggle and the account menu line up on one line, with space between them (BUD-135).
  const toolbar = [page.getByTestId('view-toggle'), page.getByTestId('day-account').getByRole('button')];
  await expectToolbarTidy(toolbar);
  const [toggle, account] = await Promise.all(toolbar.map(async control => (await control.boundingBox())!));
  expect(Math.abs((toggle.y + toggle.height / 2) - (account.y + account.height / 2)), 'one line').toBeLessThanOrEqual(1);
  // A long account name is cut short rather than pushing the menu off the screen or onto a new line.
  expect(account.x + account.width, 'the account menu stays on screen').toBeLessThanOrEqual(page.viewportSize()!.width);

  // Week or month is chosen from the period's name.
  await choosePeriod(page, 'period-name', 'Month view');
  await expect(page.getByTestId('period-name')).toHaveText(/^[A-Z][a-z]+ \d{4}$/);

  // The name always has its year, and stays put when "Go to today" appears beside it.
  const centre = async () => { const b = (await page.getByTestId('period-name').getByRole('button').boundingBox())!; return b.x + b.width / 2; };
  const before = await centre();
  await page.getByTestId('next-period').click();
  await expect(page.getByTestId('go-to-today')).toBeVisible();
  expect(Math.abs(await centre() - before), 'the name stays centred').toBeLessThanOrEqual(1);
  await page.getByTestId('go-to-today').click();
  await expect(page.getByTestId('go-to-today')).toBeHidden();
  await choosePeriod(page, 'period-name', 'Week view');
  await expect(page.getByTestId('period-name')).toContainText('–');
});
