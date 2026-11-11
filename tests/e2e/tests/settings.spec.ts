import { expect, test } from './fixtures';
import { choose, settled, signIn, groupOutgoingsBy } from './helpers';

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

  await page.getByTestId('show-year').click();
  await groupOutgoingsBy(page, 'Paid from');
  await expect(page.getByTestId('month-name')).toContainText('–');

  await page.reload();
  await settled(page);
  await expect(page.getByTestId('show-year')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('group-by')).toHaveText('Group: Paid from');
  await expect(page.getByTestId('month-name')).toContainText('–');
});
