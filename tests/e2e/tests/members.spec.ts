import { expect, test } from './fixtures';
import { choose, settled, signIn, signInAsMemberOf } from './helpers';

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
  await expect(page.getByTestId('owner-cant-leave')).toBeVisible();
});

test("an admin can change a member's role and remove them", async ({ page }) => {
  const owner = await signIn(page);
  const member = await signInAsMemberOf(page, owner, 'Viewer');
  await signIn(page, owner);

  await page.goto('/members');
  await settled(page);
  const row = page.getByTestId('member').filter({ hasText: member });
  await expect(row.getByTestId('member-role')).toHaveText('Viewer');

  await choose(page, 'role-select', 'User');
  await expect(page.getByText(`${member} is now a User.`)).toBeVisible();
  await expect(row.getByTestId('member-role')).toHaveText('User');

  await row.getByTestId('remove-member').click();
  await expect(row.getByTestId('remove-confirm')).toContainText(`Remove ${member} from this household?`);
  await row.getByTestId('remove-yes').click();
  await expect(page.getByText(`${member} was removed.`)).toBeVisible();
  await expect(page.getByTestId('member')).toHaveCount(1);
});

test('a member can leave and goes back to their own household', async ({ page }) => {
  const owner = await signIn(page);
  await signInAsMemberOf(page, owner, 'Viewer');
  await expect(page.getByTestId('household-role')).toHaveText('Your role in this household: Viewer.');

  await page.goto('/members');
  await settled(page);
  await expect(page.getByTestId('member')).toHaveCount(2);
  await page.getByTestId('leave').click();
  await page.getByTestId('leave-yes').click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByTestId('household-role')).toHaveText("You're the owner of this household.");
});

test('the owner can hand ownership to another Admin, then leave', async ({ page }) => {
  const owner = await signIn(page);
  const admin = await signInAsMemberOf(page, owner, 'Admin');
  await signIn(page, owner);

  await page.goto('/members');
  await settled(page);
  const row = page.getByTestId('member').filter({ hasText: admin });
  await row.getByTestId('make-owner').click();
  await expect(row.getByTestId('owner-confirm')).toContainText(`Make ${admin} the owner?`);
  await row.getByTestId('owner-yes').click();

  await expect(page).toHaveURL(/\/members$/);
  await settled(page);
  await expect(page.getByTestId('member').filter({ hasText: admin }).getByTestId('member-owner')).toHaveText('Owner');
  await expect(page.getByTestId('owner-cant-leave')).toHaveCount(0);
  await page.getByTestId('leave').click();
  await page.getByTestId('leave-yes').click();
  await expect(page).toHaveURL(/\/$/);
});
