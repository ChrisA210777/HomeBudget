import { expect, test } from './fixtures';
import { settled, signIn, uniqueName } from './helpers';

// ADR-015: an Admin invites someone, sends the link, and they join as the person they were invited as.

test('an invitation link brings someone into the household as their person', async ({ browser }) => {
  const ownerPage = await (await browser.newContext()).newPage();
  const owner = await signIn(ownerPage);

  // A person who doesn't sign in yet.
  await ownerPage.goto('/people');
  await settled(ownerPage);
  await ownerPage.getByTestId('add-name').fill('Jane');
  await ownerPage.getByTestId('add-person').click();
  await expect(ownerPage.getByTestId('person-name').filter({ hasText: 'Jane' })).toBeVisible();

  await ownerPage.goto('/members');
  await settled(ownerPage);
  await ownerPage.getByTestId('invite').click();
  await ownerPage.getByTestId('invite-email').fill('jane@example.com');
  await ownerPage.getByTestId('invite-person').click();
  await ownerPage.getByRole('option', { name: 'Jane' }).click();
  await ownerPage.getByTestId('invite-create').click();
  await expect(ownerPage.getByTestId('invitation-ready')).toBeVisible();
  await expect(ownerPage.getByTestId('invitation-email')).toHaveText(['jane@example.com']);

  // The Email option carries the link.
  const mailto = (await ownerPage.getByTestId('email-invitation').getAttribute('href'))!;
  const link = decodeURIComponent(mailto).match(/https?:\/\/\S+\/invite\/\S+/)![0];

  // Jane signs in (getting a household of her own, as anyone does) and opens the link.
  const janePage = await (await browser.newContext()).newPage();
  await signIn(janePage, uniqueName('Jane'));
  await janePage.goto(new URL(link).pathname);
  await expect(janePage.getByTestId('invite-summary')).toContainText(`${owner.split(' ')[0]}`);
  await expect(janePage.getByTestId('invite-summary')).toContainText('as Jane');
  await janePage.getByTestId('accept-invite').click();

  // She's in the household as Jane, and her empty starter household has gone.
  await expect(janePage.getByTestId('welcome')).toContainText('Jane');
  await janePage.goto('/members');
  await settled(janePage);
  await expect(janePage.getByTestId('member')).toHaveCount(2);

  await ownerPage.reload();
  await settled(ownerPage);
  await expect(ownerPage.getByTestId('no-invitations')).toBeVisible();
});
