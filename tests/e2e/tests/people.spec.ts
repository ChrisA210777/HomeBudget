import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

// BUD-112: Admins remove someone who doesn't sign in.
test('an Admin can remove someone who does not sign in', async ({ page }) => {
  await signIn(page);
  await page.goto('/people');
  await settled(page);

  await page.getByTestId('add-name').fill('Sam');
  await page.getByTestId('add-person').click();
  const sam = page.getByTestId('person').filter({ hasText: 'Sam' });
  await expect(sam).toBeVisible();

  // You sign in, so you can't be removed here.
  await expect(page.getByTestId('person').first().getByTestId('remove-person')).toHaveCount(0);

  await sam.getByTestId('remove-person').click();
  await expect(sam.getByTestId('remove-person-panel')).toContainText('Remove Sam from the household');
  await sam.getByTestId('remove-person-yes').click();

  await expect(page.getByText('Sam was removed.')).toBeVisible();
  await expect(page.getByTestId('person').filter({ hasText: 'Sam' })).toHaveCount(0);
});
