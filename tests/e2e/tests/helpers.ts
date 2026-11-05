import { expect, type Page, type TestInfo } from '@playwright/test';

let counter = 0;

/** A name nobody else in this test run has, so each test gets its own user and household. */
export function uniqueName(prefix: string): string {
  counter += 1;
  return `${prefix} ${Date.now().toString(36)}${counter.toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

/** Signs in as a new person (who gets "My Household") and waits for the home page. */
export async function signIn(page: Page, name = uniqueName('Tester')): Promise<string> {
  await page.goto(`/account/test-sign-in?name=${encodeURIComponent(name)}&returnUrl=%2F`);
  await expect(page.getByTestId('welcome')).toContainText(name);
  return name;
}

/** Waits until the page has finished loading its data. */
export async function settled(page: Page): Promise<void> {
  await expect(page.locator('h1').first()).toBeVisible();
  await expect(page.locator('.mud-progress-linear, .mud-progress-circular')).toHaveCount(0);
}

export function isPhone(testInfo: TestInfo): boolean {
  return testInfo.project.name === 'phone';
}

/** Adds an income through the form and returns its page URL. */
export async function addIncome(page: Page, name: string, amount: string): Promise<string> {
  await page.goto('/income/new');
  await settled(page);
  await page.getByTestId('name').fill(name);
  await page.getByTestId('amount').fill(amount);
  await page.getByTestId('save').click();
  await expect(page.getByTestId('income-name')).toHaveText(name);
  return page.url();
}
