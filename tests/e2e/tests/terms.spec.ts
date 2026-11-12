import { expect, test } from './fixtures';
import { settled, uniqueName } from './helpers';

// BUD-152: the privacy notice and terms, and accepting the terms before using the app.
test('the privacy notice and terms can be read before signing in', async ({ page }) => {
  await page.goto('/login');
  await page.getByTestId('login-legal').getByRole('link', { name: 'privacy notice' }).click();
  await expect(page).toHaveURL(/\/privacy$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Privacy notice' })).toBeVisible();

  await page.getByRole('link', { name: 'terms of use' }).click();
  await expect(page).toHaveURL(/\/terms$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Terms of use' })).toBeVisible();
});

test('someone new accepts the terms first, then carries on to where they were going', async ({ page }) => {
  await page.goto(`/account/test-sign-in?name=${encodeURIComponent(uniqueName('Terms'))}&returnUrl=%2Fbudget&acceptTerms=false`);

  await expect(page).toHaveURL(/\/terms\/accept\?returnUrl=%2Fbudget$/);
  await expect(page.getByTestId('accept-terms')).toBeDisabled();
  await page.getByRole('checkbox', { name: /I'm 18 or over/ }).check();
  await page.getByTestId('accept-terms').click();

  await expect(page).toHaveURL(/\/budget$/);
  await settled(page);

  // Not asked again.
  await page.goto('/income');
  await expect(page).toHaveURL(/\/income$/);
});
