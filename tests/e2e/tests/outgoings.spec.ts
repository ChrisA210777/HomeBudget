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
