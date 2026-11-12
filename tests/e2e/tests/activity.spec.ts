import { expect, test } from '@playwright/test';
import { addOutgoing, settled, signIn } from './helpers';

// BUD-139, BUD-140: what's been changed in the household, and by whom.
test('changes show on Home and on the Recent activity page, which can be filtered', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Window cleaning', 'Home maintenance', '15');

  await page.goto('/');
  await settled(page);
  await expect(page.getByTestId('activity-text').first()).toHaveText(/^Tester added a new .*: Window cleaning, £15\.00$/);

  await page.getByTestId('go-to-activity').click();
  await expect(page).toHaveURL(/\/activity$/);
  await expect(page.getByTestId('activity-day-name').first()).toHaveText('Today');
  await expect(page.getByTestId('activity-text').first()).toContainText('Window cleaning');

  // The entry links to the outgoing, and back comes back here.
  await page.getByTestId('activity-text').first().click();
  await expect(page).toHaveURL(/\/outgoings\/[0-9a-f-]+\?from=%2Factivity$/);

  await page.goto('/activity');
  await settled(page);
  await page.getByTestId('filter-type').getByRole('button').click();
  await page.getByTestId('filter-type-option').filter({ hasText: 'Income' }).click();
  await expect(page.getByTestId('activity-empty')).toHaveText(/No changes match these filters/);
});
