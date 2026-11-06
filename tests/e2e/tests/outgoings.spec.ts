import { expect, test } from './fixtures';
import { addOutgoing, choose, settled, signIn } from './helpers';


test("an outgoing added from the home page shows in this month's list", async ({ page }) => {
  await signIn(page);
  await page.getByTestId('add-outgoing').click();
  await settled(page);

  await page.getByTestId('name').fill('Car insurance');
  await choose(page, 'category', 'Insurance');
  await choose(page, 'priority', 'Essential');
  await page.getByTestId('amount').fill('480');
  await choose(page, 'unit', 'year');
  await expect(page.getByTestId('monthly-equivalent')).toHaveText('£480.00 a year = £40.00 a month');

  await page.getByTestId('save').click();

  await expect(page.getByRole('heading', { level: 1, name: 'Outgoings' })).toBeVisible();
  await expect(page.getByText('Car insurance was added.')).toBeVisible();
  const item = page.getByTestId('outgoing-item').filter({ hasText: 'Car insurance' });
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
