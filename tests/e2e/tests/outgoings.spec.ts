import { expect, test } from './fixtures';
import type { Page } from '@playwright/test';
import { settled, signIn } from './helpers';

/** Opens a MudSelect (found by the data-testid on its input) and picks the option containing the text. */
async function choose(page: Page, testId: string, option: string): Promise<void> {
  const select = page.locator('.mud-select').filter({ has: page.getByTestId(testId) }).first();
  const item = page.locator('.mud-popover-open .mud-list-item').filter({ hasText: option }).first();
  await select.scrollIntoViewIfNeeded();
  try {
    await expect(async () => {
      if (!(await item.isVisible())) {
        await select.click();
      }
      await expect(item, `the ${testId} list didn't open`).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 20_000 });
  } catch (error) {
    // Diagnostics for CI, where traces can't be downloaded.
    const box = await select.boundingBox();
    const report = await page.evaluate(({ x, y }) => {
      const hit = document.elementFromPoint(x, y);
      const popovers = [...document.querySelectorAll('.mud-popover')].map(p => `${p.className} :: ${(p.textContent ?? '').slice(0, 80)}`);
      return { hit: hit ? `${hit.tagName}.${hit.className} :: ${(hit.textContent ?? '').slice(0, 60)}` : 'none', popovers, scrollY: window.scrollY, height: window.innerHeight };
    }, { x: (box?.x ?? 0) + (box?.width ?? 0) / 2, y: (box?.y ?? 0) + (box?.height ?? 0) / 2 });
    console.log(`choose(${testId}) failed. Select box: ${JSON.stringify(box)}\n${JSON.stringify(report, null, 2)}`);
    throw error;
  }
  await item.click();
  await expect(page.locator('.mud-popover-open .mud-list-item')).toHaveCount(0);
}

test("an outgoing added from the home page shows in this month's list", async ({ page }) => {
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

  await expect(page.getByRole('heading', { level: 1, name: 'Outgoings' })).toBeVisible();
  await expect(page.getByText('Car insurance was added.')).toBeVisible();
  const item = page.getByTestId('outgoing-item').filter({ hasText: 'Car insurance' });
  await expect(item.getByTestId('outgoing-monthly')).toHaveText('£40.00');
  await expect(item.getByTestId('outgoing-paid')).toContainText('£480.00 a year');
  await expect(page.getByTestId('total-essential')).toHaveText('£40.00');
});

test('the add form warns about card numbers', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  await page.getByTestId('toggle-details').click();
  await page.getByTestId('reference-value').fill('4111 1111 1111 1111');

  await expect(page.getByTestId('sensitive-warning')).toBeVisible();
});
