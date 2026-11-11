import { expect, test } from './fixtures';
import { addOutgoing, choose, expectToolbarTidy, isPhone, settled, signIn } from './helpers';
import type { Locator, Page } from '@playwright/test';

// BUD-79: on the Outgoings and Income lists the title and actions stay at the top while the list
// scrolls. Larger screens keep the summary and filters too. On phones the buttons shrink to their
// icons and the filters share one line.

async function box(locator: Locator) {
  const found = await locator.boundingBox();
  expect(found, 'element should be on screen').not.toBeNull();
  return found!;
}

/** Scrolls the page to the bottom and checks that it actually moved. */
async function scrollToBottom(page: Page): Promise<void> {
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBeGreaterThan(100);
}

/** The pinned title row sits directly under the app bar. */
async function expectTitlePinned(page: Page): Promise<void> {
  const appBar = await box(page.locator('.mud-appbar'));
  const title = await box(page.getByTestId('list-head-title'));
  expect(Math.abs(title.y - (appBar.y + appBar.height)), `title top ${title.y}, app bar bottom ${appBar.y + appBar.height}`).toBeLessThanOrEqual(1);
  await expect(page.getByRole('heading', { level: 1 })).toBeInViewport();
}

test('the outgoings title, actions and (on larger screens) summary stay at the top while scrolling', async ({ page }, testInfo) => {
  const phone = isPhone(testInfo);
  await page.setViewportSize(phone ? { width: 390, height: 560 } : { width: 1280, height: 740 });
  await signIn(page);
  for (const [name, category] of [['Gym', 'Leisure'], ['Car insurance', 'Insurance'], ['Water', 'Utilities'], ['Phone', 'Utilities']]) {
    await addOutgoing(page, name, category, '20');
  }
  await page.goto('/outgoings');
  await settled(page);

  await scrollToBottom(page);

  await expectTitlePinned(page);
  await expect(page.getByTestId('add-outgoing')).toBeInViewport();
  await expect(page.getByTestId('go-to-categories')).toBeInViewport();
  if (phone) {
    // No room on a phone, so the summary scrolls away with the list.
    await expect(page.getByTestId('month-total')).not.toBeInViewport();
  } else {
    await expect(page.getByTestId('month-total')).toBeInViewport();
    await expect(page.getByTestId('filter-kind')).toBeInViewport();
  }
});

test('on a phone the outgoings actions shrink to icons and the filters are one tidy line', async ({ page }, testInfo) => {
  test.skip(!isPhone(testInfo), 'Phone layout only');
  await page.setViewportSize({ width: 360, height: 740 });
  await signIn(page);
  await page.goto('/outgoings');
  await settled(page);

  const heading = await box(page.getByRole('heading', { level: 1, name: 'Outgoings' }));
  for (const [testId, name] of [['add-outgoing', 'Add an outgoing'], ['go-to-categories', 'Categories'], ['go-to-renewals', 'Renewals']]) {
    const button = page.getByTestId(testId);
    await expect(button).toHaveAccessibleName(name);
    await expect(button.locator('.button-label')).toBeHidden();
    const buttonBox = await box(button);
    const middle = buttonBox.y + buttonBox.height / 2;
    expect(middle, `${testId} should be on the title's line`).toBeGreaterThan(heading.y);
    expect(middle, `${testId} should be on the title's line`).toBeLessThan(heading.y + heading.height);
  }

  // BUD-134: one Filter button, the Group menu and the expand/collapse icon on one line; the filters open in a panel.
  await expect(page.getByTestId('filter-kind')).toBeHidden();
  const controls = [page.getByTestId('open-filters'), page.getByTestId('group-by').getByRole('button'), page.getByTestId('toggle-all-groups')];
  await expectToolbarTidy(controls);
  const boxes = await Promise.all(controls.map(c => box(c)));
  expect(new Set(boxes.map(b => Math.round(b.y + b.height / 2))).size, 'the toolbar should fit on one line').toBe(1);
  for (const control of boxes) {
    expect(control.x + control.width).toBeLessThanOrEqual(360);
  }

  await page.getByTestId('open-filters').click();
  await expect(page.getByTestId('filter-sheet')).toBeVisible();
  await choose(page, 'panel-priority', 'Optional');
  await page.getByTestId('close-filters').click();
  await expect(page.getByTestId('filter-sheet')).toBeHidden();
  await expect(page.getByTestId('open-filters')).toHaveText('Filter (1)');
  await expect(page.getByTestId('active-filter')).toHaveText(['Optional']);
});

test('the income title and action stay at the top while scrolling', async ({ page }, testInfo) => {
  const phone = isPhone(testInfo);
  await page.setViewportSize(phone ? { width: 360, height: 420 } : { width: 1280, height: 740 });
  await signIn(page);
  await page.goto('/income');
  await settled(page);

  // A short page may not scroll; pad the end of the page so it does.
  await page.evaluate(() => {
    const spacer = document.createElement('div');
    spacer.style.height = '2000px';
    document.querySelector('.list-head')!.parentElement!.appendChild(spacer);
  });
  await scrollToBottom(page);

  await expectTitlePinned(page);
  await expect(page.getByTestId('add-income')).toBeInViewport();
  await expect(page.getByTestId('add-income')).toHaveAccessibleName('Add income');
  if (phone) {
    await expect(page.getByTestId('add-income').locator('.button-label')).toBeHidden();
  } else {
    await expect(page.getByTestId('month-total')).toBeInViewport();
  }
});

// BUD-125, BUD-129: an item's actions are one row of tiles on a phone, and the usual buttons on larger screens. Either
// way, Change amount is filled as the main action.
test("an outgoing's actions fit on one line, with Change amount as the main action", async ({ page }, testInfo) => {
  await signIn(page);
  const url = await addOutgoing(page, 'Window cleaner', 'Home maintenance', '15');
  await page.goto(url);
  await settled(page);

  const actions = page.locator('[data-testid=change-amount], [data-testid=pause], [data-testid=end-outgoing], [data-testid=edit-details], [data-testid=delete-outgoing]');
  await expect(actions).toHaveCount(5);
  const tops = await actions.evaluateAll(buttons => buttons.map(b => Math.round(b.getBoundingClientRect().top)));
  expect(new Set(tops).size, 'the actions should share one line').toBe(1);
  await expect(page.getByTestId('change-amount')).toHaveClass(/mud-button-filled/);
  await expect(page.getByTestId('change-amount')).toHaveText(isPhone(testInfo) ? /^amount$/i : /^change amount$/i, { useInnerText: true });
});
