import { expect, type Locator, type Page, type TestInfo } from '@playwright/test';

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

/**
 * Checks a page's toolbar (BUD-124, BUD-130): controls on the same line are centred on one another and have a gap
 * between them. Pass the locators of the visible controls, left to right.
 */
export async function expectToolbarTidy(controls: Locator[]): Promise<void> {
  const boxes = [];
  for (const control of controls) {
    const box = await control.boundingBox();
    expect(box, 'toolbar control should be visible').not.toBeNull();
    boxes.push(box!);
  }
  for (let i = 1; i < boxes.length; i++) {
    const [a, b] = [boxes[i - 1], boxes[i]];
    const [aMiddle, bMiddle] = [a.y + a.height / 2, b.y + b.height / 2];
    if (Math.abs(aMiddle - bMiddle) > a.height / 2) {
      continue; // wrapped onto the next line
    }
    expect(Math.abs(aMiddle - bMiddle), 'toolbar controls should be centred on one line').toBeLessThanOrEqual(1);
    expect(b.x - (a.x + a.width), 'toolbar controls should have a gap between them').toBeGreaterThanOrEqual(6);
  }
}

/** Groups the Outgoings list from its Group menu (BUD-134). */
export async function groupOutgoingsBy(page: Page, grouping: 'Category' | 'Paid from'): Promise<void> {
  await page.getByTestId('group-by').getByRole('button').click();
  await page.getByTestId('group-by-option').filter({ hasText: grouping }).click();
  await expect(page.getByTestId('group-by')).toHaveText(`Group: ${grouping}`);
}

/** Opens a page from the main navigation: the side drawer, or on phones the bottom bar or its More page (BUD-115, BUD-116). */
export async function openFromNav(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  if (isPhone(testInfo)) {
    const bar = page.getByTestId('bottom-nav').getByRole('link', { name, exact: true });
    if (await bar.count() === 0) {
      await page.getByTestId('bottom-nav').getByRole('link', { name: 'More', exact: true }).click();
      await page.getByTestId('more-link').filter({ hasText: new RegExp(`^\\s*${name}\\s*$`) }).click();
      return;
    }
    await bar.click();
  } else {
    await page.getByTestId('side-nav').getByRole('link', { name, exact: true }).click();
  }
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
