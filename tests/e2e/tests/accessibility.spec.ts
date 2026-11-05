import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './fixtures';
import type { Page } from '@playwright/test';
import { addIncome, settled, signIn } from './helpers';

const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function expectNoSeriousViolations(page: Page, what: string): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(tags).analyze();
  const serious = results.violations
    .filter(v => v.impact === 'serious' || v.impact === 'critical')
    .map(v => `${v.id} (${v.impact}): ${v.help}\n    ${v.nodes.slice(0, 5).map(n => n.target.join(' ')).join('\n    ')}`);
  expect(serious, `${what}:\n${serious.join('\n')}`).toEqual([]);
}

for (const theme of ['Light', 'Dark']) {
  test.describe(`${theme} theme`, () => {
    test.beforeEach(async ({ page }) => {
      await page.addInitScript(value => window.localStorage.setItem('budget.theme', value), theme);
    });

    test('main pages have no serious accessibility problems', async ({ page }) => {
      await signIn(page);
      const incomePage = await addIncome(page, 'Salary', '3000');

      for (const path of ['/', '/income', '/income/new', '/people', '/outgoings/categories', '/outgoings/new', new URL(incomePage).pathname]) {
        await page.goto(path);
        await settled(page);
        await expectNoSeriousViolations(page, `${path} (${theme})`);
      }

      await page.goto('/');
      await settled(page);
      await page.getByTestId('user-menu').getByRole('button').first().click();
      await expect(page.getByTestId('theme-dark')).toBeVisible();
      await expectNoSeriousViolations(page, `user menu (${theme})`);
    });

    test('the sign-in page has no serious accessibility problems', async ({ page }) => {
      await page.goto('/login');
      await settled(page);
      await expectNoSeriousViolations(page, `/login (${theme})`);
    });
  });
}
