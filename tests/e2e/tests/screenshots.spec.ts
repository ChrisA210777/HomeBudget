import { test } from './fixtures';
import { addIncome, settled, signIn } from './helpers';

// Not a check: saves pictures of each main page in both themes for people to look over
// (uploaded by CI as the "e2e-screens" artifact).
for (const theme of ['Light', 'Dark']) {
  test(`screenshots (${theme})`, async ({ page }, testInfo) => {
    await page.addInitScript(value => window.localStorage.setItem('budget.theme', value), theme);
    await signIn(page, 'Chris Example');
    const incomePage = await addIncome(page, 'Chris – salary', '3000');

    const pages: [string, string][] = [['home', '/'], ['income', '/income'], ['income-detail', new URL(incomePage).pathname], ['people', '/people']];
    for (const [name, path] of pages) {
      await page.goto(path);
      await settled(page);
      await page.screenshot({ path: `screens/${testInfo.project.name}-${theme.toLowerCase()}-${name}.png`, fullPage: true });
    }
  });
}
