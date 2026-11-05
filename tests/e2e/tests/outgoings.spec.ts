import { expect, test } from './fixtures';
import type { Page } from '@playwright/test';
import { settled, signIn } from './helpers';

/** Opens a MudSelect (found by the data-testid on its input) and picks the option containing the text. */
async function choose(page: Page, testId: string, option: string): Promise<void> {
  const select = page.locator('.mud-select').filter({ has: page.getByTestId(testId) }).first();
  const item = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: option }).first();
  await select.scrollIntoViewIfNeeded();
  await expect(async () => {
    if (!(await item.isVisible())) {
      await select.click();
    }
    await expect(item, `the ${testId} list didn't open`).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 20_000 });
  await item.click();
  await expect(page.locator('.mud-popover-open .mud-list-item')).toHaveCount(0);
}

test('an outgoing can be added from the home page', async ({ page }) => {
  await signIn(page);
  await page.getByTestId('add-outgoing').click();
  await settled(page);

  await page.getByTestId('name').fill('Car insurance');
  await choose(page, 'category', 'Insurance');
  await choose(page, 'priority', 'Essential');
  await page.getByTestId('amount').fill('480');
  await choose(page, 'unit', 'year');
  await expect(page.getByTestId('monthly-equivalent')).toHaveText('£480.00 a year = £40.00 a month');

  await page.getByTestId('save').click();

  await expect(page.getByTestId('welcome')).toBeVisible();
  await expect(page.getByText('Car insurance was added.')).toBeVisible();
});

test('the add form warns about card numbers', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  await page.getByTestId('toggle-details').click();
  await page.getByTestId('reference-value').fill('4111 1111 1111 1111');

  await expect(page.getByTestId('sensitive-warning')).toBeVisible();
});
