import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

test('you can create another household and switch between them from the app bar', async ({ page }) => {
  await signIn(page);
  await expect(page.getByTestId('appbar-household')).toHaveText('My Household');

  await page.getByTestId('appbar-household').getByRole('button').click();
  await page.getByTestId('create-household').click();
  await expect(page).toHaveURL(/\/households\/new$/);
  await settled(page);
  await expect(page.getByTestId('household-name')).toHaveValue('My Household');
  await page.getByTestId('household-name').fill('Holiday fund');
  await page.getByTestId('create').click();

  await expect(page.getByTestId('appbar-household')).toHaveText('Holiday fund');
  await expect(page.getByTestId('household-role')).toHaveText("You're the owner of this household.");

  await page.getByTestId('appbar-household').getByRole('button').click();
  const choices = page.getByTestId('switch-household');
  await expect(choices).toHaveCount(2);
  await expect(choices.filter({ hasText: 'Holiday fund' })).toHaveAttribute('data-active', 'true');
  await choices.filter({ hasText: 'My Household' }).click();

  await expect(page.getByTestId('appbar-household')).toHaveText('My Household');

  // Remembered for next time.
  await page.reload();
  await expect(page.getByTestId('appbar-household')).toHaveText('My Household');
});
