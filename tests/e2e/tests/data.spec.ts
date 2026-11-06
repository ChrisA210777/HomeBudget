import { readFile } from 'node:fs/promises';
import { expect, test } from './fixtures';
import { addOutgoing, settled, signIn } from './helpers';

test('an admin can download the household as a JSON file', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');

  await page.goto('/');
  await page.getByTestId('appbar-household').getByRole('button').click();
  await page.getByTestId('household-data').click();
  await expect(page).toHaveURL(/\/household\/data$/);
  await settled(page);
  await expect(page.getByTestId('export-warning')).toBeVisible();

  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-json').click()]);
  expect(download.suggestedFilename()).toMatch(/^home-budget-my-household-\d{4}-\d{2}-\d{2}\.json$/);

  const file = JSON.parse(await readFile((await download.path())!, 'utf8'));
  expect(file.format).toBe('home-budget-export');
  expect(file.schemaVersion).toBe(1);
  expect(file.household.name).toBe('My Household');
  expect(file.outgoings.map((o: { name: string }) => o.name)).toEqual(['Council tax']);
});
