import { expect, test } from './fixtures';
import { addIncome, addOutgoing, settled, signIn } from './helpers';

test('a new household is shown its next steps, which go once done', async ({ page }) => {
  await signIn(page);

  const steps = page.getByTestId('getting-started').locator('li');
  await expect(steps).toHaveCount(3);
  await expect(page.getByTestId('setup-add-income')).toContainText('Add your income');
  await expect(page.getByTestId('income-card')).toHaveCount(0);

  await addIncome(page, 'Salary', '2500');
  await page.goto('/');
  await expect(page.getByTestId('setup-add-outgoing')).toBeVisible();
  await expect(page.getByTestId('setup-add-income')).toHaveCount(0);

  await page.getByTestId('setup-people-not-now').click();
  await expect(page.getByTestId('setup-add-people')).toHaveCount(0);
  await page.reload();
  await expect(page.getByTestId('setup-add-outgoing')).toBeVisible();
  await expect(page.getByTestId('setup-add-people')).toHaveCount(0);
});

test('categories are reached from the outgoings page', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings');
  await page.getByTestId('go-to-categories').click();
  await expect(page).toHaveURL(/\/outgoings\/categories$/);
});

test("a category's count opens its outgoings", async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Gym', 'Leisure', '20');
  await page.goto('/outgoings/categories');
  await settled(page);

  await page.getByTestId('category').filter({ hasText: 'Leisure' }).getByTestId('category-usage').click();

  await expect(page).toHaveURL(/\/outgoings\?category=/);
  await expect(page.getByTestId('clear-category')).toBeVisible();
  await expect(page.getByTestId('outgoing-name')).toHaveText(['Gym']);
});

test('a category icon can be changed', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/categories');
  const housing = page.getByTestId('category').filter({ hasText: 'Housing' });
  await expect(housing.getByTestId('category-icon')).toHaveAttribute('data-icon', 'home');

  await housing.getByTestId('rename').click();
  // While renaming, the name is in a text box, so the row no longer matches by its text.
  await page.getByRole('radiogroup', { name: 'Icon for Housing' }).getByRole('radio', { name: 'Bank' }).click();
  await page.getByTestId('rename-save').click();
  await expect(housing.getByTestId('category-icon')).toHaveAttribute('data-icon', 'bank');
});
