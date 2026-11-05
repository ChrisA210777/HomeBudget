import { expect, test } from './fixtures';
import type { Page } from '@playwright/test';
import { signIn } from './helpers';

const lightBackground = 'rgb(247, 249, 249)';
const darkBackground = 'rgb(18, 18, 18)';

async function background(page: Page): Promise<string> {
  return page.evaluate(() => getComputedStyle(document.body).backgroundColor);
}

test.describe('on a device set to light mode', () => {
  test.use({ colorScheme: 'light' });

  test('follows the device, and a chosen theme is remembered', async ({ page }) => {
    await signIn(page);
    await expect.poll(() => background(page)).toBe(lightBackground);

    await page.getByTestId('user-menu').getByRole('button').first().click();
    await page.getByTestId('theme-dark').click();
    await expect.poll(() => background(page)).toBe(darkBackground);

    await page.reload();
    await expect(page.getByTestId('welcome')).toBeVisible();
    await expect.poll(() => background(page)).toBe(darkBackground);

    await page.getByTestId('user-menu').getByRole('button').first().click();
    await page.getByTestId('theme-system').click();
    await expect.poll(() => background(page)).toBe(lightBackground);
  });
});

test.describe('on a device set to dark mode', () => {
  test.use({ colorScheme: 'dark' });

  test('starts dark', async ({ page }) => {
    await signIn(page);
    await expect.poll(() => background(page)).toBe(darkBackground);
  });
});
