import { expect, test } from './fixtures';
import { choose, settled, signIn } from './helpers';

// BUD-87: settings are saved with the account, so they're still there after reloading.

test('settings are reached from the account menu and kept after reloading', async ({ page }) => {
  await signIn(page);

  await page.getByTestId('user-menu').click();
  await page.getByTestId('settings').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  await settled(page);

  await choose(page, 'first-day-of-week', 'Monday');
  await expect(page.getByText('Saved.')).toBeVisible();

  await page.reload();
  await settled(page);
  await expect(page.getByTestId('first-day-of-week')).toHaveValue('Monday');
});

test('outgoings open in the view chosen last time', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings');
  await settled(page);

  await page.getByTestId('show-year').click();
  await page.getByTestId('group-paid-from').click();
  await expect(page.getByTestId('month-name')).toContainText('–');

  await page.reload();
  await settled(page);
  await expect(page.getByTestId('show-year')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('group-paid-from')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('month-name')).toContainText('–');
});
