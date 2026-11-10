import { expect, test } from './fixtures';
import { addOutgoing, choose, settled, signIn, typeOutgoingName } from './helpers';


test("an outgoing added from the home page shows in this month's list", async ({ page }) => {
  await signIn(page);
  await page.getByTestId('setup-add-outgoing-go').click();
  await settled(page);

  await typeOutgoingName(page, 'Insurance for the car');
  await choose(page, 'category', 'Insurance');
  await choose(page, 'priority', 'Essential');
  await page.getByTestId('amount').fill('480');
  await choose(page, 'unit', 'year');
  await expect(page.getByTestId('monthly-equivalent')).toHaveText('£480.00 a year = £40.00 a month');

  await page.getByTestId('save').click();

  await expect(page.getByRole('heading', { level: 1, name: 'Outgoings' })).toBeVisible();
  await expect(page.getByText('Insurance for the car was added.')).toBeVisible();
  const item = page.getByTestId('outgoing-item').filter({ hasText: 'Insurance for the car' });
  // It starts this month, so this month counts in full (ADR-011).
  await expect(item.getByTestId('outgoing-monthly')).toHaveText('£40.00');
  await expect(item.getByTestId('outgoing-paid')).toContainText('£480.00 a year');
  await expect(item.getByTestId('outgoing-note').first()).toContainText('Starts');
  await expect(page.getByTestId('total-essential')).toHaveText('£40.00');
});

test('the add form warns about card numbers', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  await page.getByTestId('toggle-details').click();
  await page.getByTestId('reference-value').fill('4111 1111 1111 1111');

  await expect(page.getByTestId('sensitive-warning')).toBeVisible();
});

test('an outgoing can be paused for whole payments and its price changed', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Gym membership', 'Leisure', '30');

  await page.getByTestId('pause').click();
  await page.getByTestId('skip-payments').fill('2');
  await expect(page.getByTestId('resumes-preview')).toContainText('Payments resume on');
  await page.getByTestId('comment').fill('Injured');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('period-description')).toContainText('skips 2 payments');

  await page.getByTestId('change-amount').click();
  await page.getByTestId('amount').fill('35');
  await page.getByTestId('comment').fill('Price rise');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('amount-change')).toHaveCount(2);

  await page.getByTestId('edit-details').click();
  await settled(page);
  await page.getByTestId('name').fill('Gym');
  await page.getByTestId('save').click();
  await expect(page.getByTestId('outgoing-name')).toHaveText('Gym');
});

test('the budget shows what is left and links to a category', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');

  await page.goto('/budget');
  await settled(page);
  await expect(page.getByTestId('budget-optional')).toHaveText('− £150.00');
  await expect(page.getByTestId('left-everything')).toHaveText('Short by £150.00');

  // BUD-83: it started this month, so the budget is £150 worse than last month, and says why.
  await expect(page.getByTestId('what-changed-summary')).toContainText('£150.00 worse than');
  await page.getByTestId('what-changed-toggle').click();
  const change = page.getByTestId('change');
  await expect(change.getByTestId('change-name')).toHaveText('Council tax');
  await expect(change.getByTestId('change-reason')).toHaveText('New');
  await expect(change.getByTestId('change-effect')).toHaveText('£150.00 less left');

  await page.getByTestId('category-link').filter({ hasText: 'Housing' }).click();
  await expect(page.getByTestId('category-filter')).toContainText('Housing');
  await expect(page.getByTestId('outgoing-name')).toHaveText(['Council tax']);
});

test('amounts show pennies, Every is centred and the period is labelled Period', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  const amount = page.getByTestId('amount');
  await amount.fill('12');
  await amount.press('Tab');
  await expect(amount).toHaveValue('12.00');

  await amount.fill('12.5');
  await amount.press('Tab');
  await expect(amount).toHaveValue('12.50');

  await expect(page.getByTestId('every')).toHaveCSS('text-align', 'center');
  await expect(page.locator('.mud-select').filter({ has: page.getByTestId('unit') }).first()).toContainText('Period');
});

test('reference names are suggested as you type', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  await page.getByTestId('toggle-details').click();
  await page.getByTestId('reference-label').fill('memb');
  await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: 'Membership number' }).first().click();
  await expect(page.getByTestId('reference-label')).toHaveValue('Membership number');
});

test('common outgoings are suggested and fill in their usual settings', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Netflix', 'Subscriptions', '10.99');

  await page.goto('/outgoings/new');
  await settled(page);
  await page.getByTestId('name').fill('netf');
  const netflix = page.locator('.mud-popover-open [data-testid=name-suggestion]').filter({ hasText: 'Netflix' });
  await expect(netflix.getByTestId('already-added')).toBeVisible();

  await page.getByTestId('name').fill('car ins');
  await page.locator('.mud-popover-open [data-testid=name-suggestion]').filter({ hasText: 'Car insurance' }).first().click();
  await expect(page.getByTestId('name')).toHaveValue('Car insurance');
  await expect(page.getByTestId('reference-label')).toHaveValue('Policy number');
  await expect(page.getByTestId('renewal-date')).toBeVisible();

  await page.getByTestId('amount').fill('480');
  await expect(page.getByTestId('monthly-equivalent')).toHaveText('£480.00 a year = £40.00 a month');
});

test('amounts, their period and the category subtotal line up on the right', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');
  await page.goto('/outgoings');
  await settled(page);

  const item = page.getByTestId('outgoing-item').first();
  const right = async (locator: import('@playwright/test').Locator) => {
    const box = (await locator.boundingBox())!;
    return box.x + box.width;
  };
  const amount = await right(item.getByTestId('outgoing-monthly'));
  expect(Math.abs((await right(item.getByTestId('outgoing-period'))) - amount)).toBeLessThanOrEqual(1);
  expect(Math.abs((await right(page.getByTestId('category-subtotal').first())) - amount)).toBeLessThanOrEqual(1);
});

test('outgoings can be shown for 12 months and grouped by the account they are paid from', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Hedge trimming fund', 'Home maintenance', '20');
  await page.goto('/outgoings');
  await settled(page);

  const item = page.getByTestId('outgoing-item').filter({ hasText: 'Hedge trimming fund' });
  await expect(item.getByTestId('outgoing-monthly')).toHaveText('£20.00');

  // BUD-80: a year adds up the 12 monthly budget amounts, starting with the shown month.
  await page.getByTestId('show-year').click();
  await expect(page.getByTestId('month-name')).toContainText('–');
  await expect(item.getByTestId('outgoing-monthly')).toHaveText('£240.00');
  await expect(item.getByTestId('outgoing-period')).toHaveText('over 12 months');

  await page.getByTestId('group-paid-from').click();
  await expect(page.getByTestId('paid-from-name')).toHaveText(['Not set']);
});

test('groups close to their header, one at a time or all at once', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Hedge trimming fund', 'Home maintenance', '20');
  await page.goto('/outgoings');
  await settled(page);

  // BUD-88: the header opens and closes its group, and keeps showing the total.
  const group = page.getByTestId('category-group').filter({ hasText: 'Home maintenance' });
  const header = group.getByTestId('group-header');
  await header.click();
  await expect(header).toHaveAttribute('aria-expanded', 'false');
  await expect(group.getByTestId('outgoing-name')).toBeHidden();
  await expect(group.getByTestId('category-subtotal')).toHaveText('£20.00');

  await page.getByTestId('toggle-all-groups').click();
  await expect(page.getByTestId('toggle-all-groups')).toHaveText('Collapse all');
  await expect(group.getByTestId('outgoing-name')).toBeVisible();

  await page.getByTestId('toggle-all-groups').click();
  await expect(group.getByTestId('outgoing-name')).toBeHidden();
  await expect(page.getByTestId('toggle-all-groups')).toHaveText('Expand all');
});

test('upcoming payments show what is due each day, a week or a month at a time', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Hedge trimming fund', 'Home maintenance', '20');
  await page.goto('/outgoings');
  await settled(page);

  // BUD-82: a monthly outgoing starting today is due today, at its real amount.
  await page.getByTestId('go-to-upcoming').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Upcoming payments' })).toBeVisible();
  await settled(page);
  const today = page.getByTestId('payment-day').filter({ has: page.getByTestId('payment-today') });
  await expect(today.getByTestId('payment-name')).toHaveText('Hedge trimming fund');
  await expect(today.getByTestId('payment-amount')).toHaveText('£20.00');

  await page.getByTestId('show-month').click();
  await expect(page.getByTestId('period-total')).toHaveText('£20.00');
  await page.getByTestId('next-period').click();
  await expect(page.getByTestId('payment-name')).toHaveText(['Hedge trimming fund']);
  await page.getByTestId('go-to-today').click();
  await expect(page.getByTestId('payment-today')).toBeVisible();
});

test('the renewals calendar is reached from upcoming payments', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/upcoming');
  await settled(page);

  // BUD-81: a new household has nothing with a renewal date yet.
  await page.getByTestId('go-to-renewals').click();
  await expect(page.getByRole('heading', { level: 1, name: 'Renewals' })).toBeVisible();
  await expect(page.getByTestId('renewals-empty')).toContainText('Nothing renews in the next 12 months');
});
