import { expect, test } from './fixtures';
import { addIncome, isPhone, settled, signIn } from './helpers';

test('shows the household in the app bar and navigation that suits the screen', async ({ page }, testInfo) => {
  await signIn(page);

  await expect(page.getByTestId('appbar-household')).toHaveText('My Household');
  if (isPhone(testInfo)) {
    await expect(page.getByTestId('bottom-nav')).toBeVisible();
    await expect(page.getByTestId('side-nav')).toBeHidden();
  } else {
    await expect(page.getByTestId('side-nav')).toBeVisible();
    await expect(page.getByTestId('bottom-nav')).toBeHidden();
  }
});

test('moves between sections with the navigation', async ({ page }, testInfo) => {
  await signIn(page);
  const nav = page.getByTestId(isPhone(testInfo) ? 'bottom-nav' : 'side-nav');

  await nav.getByRole('link', { name: 'Income' }).click();
  await expect(page).toHaveURL(/\/income$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Income' })).toBeVisible();

  await nav.getByRole('link', { name: 'People' }).click();
  await expect(page).toHaveURL(/\/people$/);
  await expect(page.getByRole('heading', { level: 1, name: 'People' })).toBeVisible();

  await nav.getByRole('link', { name: 'Home' }).click();
  await expect(page.getByTestId('welcome')).toBeVisible();
});

test('nothing scrolls sideways', async ({ page }) => {
  await signIn(page);
  const incomePage = await addIncome(page, 'Salary', '2500');

  for (const path of ['/', '/income', '/income/new', '/people', new URL(incomePage).pathname]) {
    await page.goto(path);
    await settled(page);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `${path} scrolls sideways by ${overflow}px`).toBeLessThanOrEqual(0);
  }
});

test('touch targets on phones are at least 44px', async ({ page }, testInfo) => {
  test.skip(!isPhone(testInfo), 'Phone layout only');
  await signIn(page);

  const targets = page.locator('[data-testid=bottom-nav] a, .mud-appbar button, .mud-appbar a, main button:visible');
  const count = await targets.count();
  expect(count).toBeGreaterThan(3);
  for (let i = 0; i < count; i++) {
    const box = await targets.nth(i).boundingBox();
    if (!box) continue;
    const label = await targets.nth(i).evaluate(el => el.getAttribute('aria-label') ?? el.textContent?.trim() ?? el.tagName);
    expect(box.height, `${label} is ${box.height}px tall`).toBeGreaterThanOrEqual(44);
    expect(box.width, `${label} is ${box.width}px wide`).toBeGreaterThanOrEqual(44);
  }
});

test('signed-out people only see the sign-in page', async ({ page }) => {
  await page.goto('/income');

  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expect(page.getByTestId('bottom-nav')).toHaveCount(0);
  await expect(page.getByTestId('side-nav')).toHaveCount(0);
});
