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
  // Home greets you by your first name (your person's name), e.g. "Tester" for "Tester abc123".
  await expect(page.getByTestId('welcome')).toContainText(name.split(' ')[0]);
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

/** Opens a MudSelect (found by the data-testid on its input) and picks the option containing the text. */
export async function choose(page: Page, testId: string, option: string): Promise<void> {
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

/** Adds a monthly bill starting today and returns its details page URL. */
export async function addOutgoing(page: Page, name: string, category: string, amount: string): Promise<string> {
  await page.goto('/outgoings/new');
  await settled(page);
  await typeOutgoingName(page, name);
  await choose(page, 'category', category);
  await choose(page, 'priority', 'Optional');
  await page.getByTestId('amount').fill(amount);
  await page.getByTestId('save').click();
  await page.getByTestId('outgoing-name').filter({ hasText: name }).first().click();
  await expect(page.getByTestId('outgoing-name')).toHaveText(name);
  return page.url();
}

/** Types an outgoing's name and closes the suggestions so they don't cover the rest of the form. */
export async function typeOutgoingName(page: Page, name: string): Promise<void> {
  await page.getByTestId('name').fill(name);
  await page.getByTestId('name').press('Escape');
}

/**
 * Signs in as a new person who also joins the household owned by <paramref name="ownerName"/> with
 * <paramref name="role"/>, using the test sign-in (until invitations exist, BUD-23).
 */
export async function signInAsMemberOf(page: Page, ownerName: string, role: 'Viewer' | 'User' | 'Admin', name = uniqueName('Member')): Promise<string> {
  await page.goto(`/account/test-sign-in?name=${encodeURIComponent(name)}&joinHouseholdOf=${encodeURIComponent(ownerName)}&joinAs=${role}&returnUrl=%2F`);
  // Home greets you by your first name (your person's name), e.g. "Tester" for "Tester abc123".
  await expect(page.getByTestId('welcome')).toContainText(name.split(' ')[0]);
  return name;
}
