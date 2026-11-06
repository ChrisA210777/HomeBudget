import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

test('members are listed with their role, owner badge and join date', async ({ page }) => {
  const name = await signIn(page);
  await page.goto('/people');
  await settled(page);
  await page.getByTestId('go-to-members').click();
  await expect(page).toHaveURL(/\/members$/);
  await settled(page);

  const me = page.getByTestId('member');
  await expect(me).toHaveCount(1);
  await expect(me.getByTestId('member-name')).toHaveText(name);
  await expect(me.getByTestId('member-you')).toBeVisible();
  await expect(me.getByTestId('member-role')).toHaveText('Admin');
  await expect(me.getByTestId('member-owner')).toHaveText('Owner');
  await expect(me.getByTestId('member-joined')).toHaveText(/^Joined \d{1,2} \w{3} \d{4}$/);
});
