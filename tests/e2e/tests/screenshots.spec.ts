import { test } from './fixtures';
import { addIncome, settled, signIn } from './helpers';

// Not a check: saves pictures of each main page in both themes for people to look over
// (uploaded by CI as the "e2e-screens" artifact).
for (const theme of ['Light', 'Dark']) {
  test(`screenshots (${theme})`, async ({ page }, testInfo) => {
    await page.addInitScript(value => window.localStorage.setItem('budget.theme', value), theme);
    await signIn(page, `Chris Example ${theme}`);
    const incomePage = await addIncome(page, 'Chris – salary', '3000');

    const pages: [string, string][] = [['home', '/'], ['income', '/income'], ['income-detail', new URL(incomePage).pathname], ['people', '/people']];
    for (const [name, path] of pages) {
      await page.goto(path);
      await settled(page);
      const file = `${testInfo.project.name}-${theme.toLowerCase()}-${name}`;
      await page.screenshot({ path: `screens/${file}.png`, fullPage: true });

      // For reviewing from the CI log when artifacts can't be downloaded (opt-in, manual runs only).
      if (process.env.PRINT_SCREENS === 'true') {
        const jpeg = await page.screenshot({ type: 'jpeg', quality: 45, scale: 'css' });
        console.log(`SCREEN ${file} ${jpeg.toString('base64')}`);
      }
    }
  });
}
