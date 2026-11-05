import { expect, test, type Page } from '@playwright/test';
import { addIncome, settled, signIn } from './helpers';

/** Presses Tab until the element with the test id has focus. */
async function tabTo(page: Page, testId: string, maxPresses = 40): Promise<void> {
  for (let i = 0; i < maxPresses; i++) {
    await page.keyboard.press('Tab');
    const focused = await page.evaluate(() => document.activeElement?.closest('[data-testid]')?.getAttribute('data-testid'));
    if (focused === testId) return;
  }
  throw new Error(`Couldn't reach ${testId} with the Tab key`);
}

test('adds a person using only the keyboard', async ({ page }) => {
  await signIn(page);
  await page.goto('/people');
  await settled(page);

  await tabTo(page, 'add-name');
  await page.keyboard.type('Jamie');
  await tabTo(page, 'add-person', 5);
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('person-name').filter({ hasText: 'Jamie' })).toBeVisible();
});

test('adds an income using only the keyboard', async ({ page }) => {
  await signIn(page);
  await page.goto('/income');
  await settled(page);

  await tabTo(page, 'add-income');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1, name: 'Add income' })).toBeVisible();

  await tabTo(page, 'name');
  await page.keyboard.type('Keyboard salary');
  await tabTo(page, 'amount');
  await page.keyboard.type('1800');
  await tabTo(page, 'save');
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('income-name')).toHaveText('Keyboard salary');
  await expect(page.getByTestId('amount-today')).toHaveText('£1,800.00');
});

test('records a pay rise using only the keyboard', async ({ page }) => {
  await signIn(page);
  await addIncome(page, 'Salary', '2000');

  await tabTo(page, 'change-amount');
  await page.keyboard.press('Enter');
  await tabTo(page, 'amount');
  await page.keyboard.type('2100');
  await tabTo(page, 'comment');
  await page.keyboard.type('5% pay rise');
  await tabTo(page, 'save');
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('amount-today')).toHaveText('£2,100.00');
});

test('renames the household using only the keyboard', async ({ page }) => {
  await signIn(page);

  await tabTo(page, 'rename-household');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('rename-input')).toBeFocused();
  await page.keyboard.press('ControlOrMeta+a');
  await page.keyboard.type('Keyboard Family');
  await page.keyboard.press('Enter');

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Keyboard Family');
  await expect(page.getByTestId('appbar-household')).toHaveText('Keyboard Family');
});
