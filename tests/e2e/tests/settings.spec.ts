import { expect, test } from './fixtures';
import { choose, choosePeriod, groupOutgoingsBy, settled, signIn } from './helpers';

// BUD-87: settings are saved with the account, so they're still there after reloading.

test('settings are reached from the account menu and kept after reloading', async ({ page }) => {
  await signIn(page);

  await page.getByTestId('user-menu').click();
  await page.getByTestId('settings').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  await settled(page);

  await choose(page, 'first-day-of-week', 'Monday');
  await expect(page.getByText('Saved.')).toBeVisible();

  // Saved with the account: it's what the server now returns, and the page shows it after reloading.
  const saved = await page.request.get('/bff/user/settings');
  expect((await saved.json()).firstDayOfWeek).toBe('Monday');
  await page.reload();
  await settled(page);
  const day = page.locator('.mud-select').filter({ has: page.getByTestId('first-day-of-week') });
  await expect(day.locator('input').first()).toHaveValue('Monday');
});

test('outgoings open in the view chosen last time', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings');
  await settled(page);

  await choosePeriod(page, 'month-name', '12-month view');
  await groupOutgoingsBy(page, 'Paid from');
  await expect(page.getByTestId('month-name')).toContainText('–');

  await page.reload();
  await settled(page);
  await expect(page.getByTestId('group-by')).toHaveText('Group: Paid from');
  await expect(page.getByTestId('month-name')).toContainText('–');
});

test('deleting your account signs you out and says it has gone', async ({ page }) => {
  await signIn(page);
  await page.goto('/settings');
  await settled(page);

  await page.getByTestId('delete-account').click();
  await expect(page.getByTestId('households-to-delete')).toContainText('My Household');
  await expect(page.getByTestId('delete-account-yes')).toBeDisabled();
  await page.getByTestId('delete-account-confirm').fill('DELETE');
  await page.getByTestId('delete-account-yes').click();

  await expect(page).toHaveURL(/\/login\?accountDeleted=true$/);
  await expect(page.getByTestId('account-deleted')).toHaveText('Your account has been deleted.');

  // Nothing is left signed in.
  await page.goto('/settings');
  await expect(page).toHaveURL(/\/login/);
});
