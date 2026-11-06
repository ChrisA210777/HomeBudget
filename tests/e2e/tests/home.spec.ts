import { expect, test } from './fixtures';
import { addIncome, signIn } from './helpers';

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
