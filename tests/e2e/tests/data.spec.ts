import { readFile } from 'node:fs/promises';
import { expect, test } from './fixtures';
import { addOutgoing, openFromNav, settled, signIn } from './helpers';

test('an admin can download the household as a JSON file', async ({ page }, testInfo) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');

  await page.goto('/');
  await openFromNav(page, testInfo, 'Import or export data');
  await expect(page).toHaveURL(/\/household\/data$/);
  await settled(page);
  await expect(page.getByTestId('export-warning')).toBeVisible();

  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-json').click()]);
  expect(download.suggestedFilename()).toMatch(/^home-budget-my-household-\d{4}-\d{2}-\d{2}\.json$/);

  const file = JSON.parse(await readFile((await download.path())!, 'utf8'));
  expect(file.format).toBe('home-budget-export');
  expect(file.schemaVersion).toBe(5);
  expect(file.household.name).toBe('My Household');
  expect(file.outgoings.map((o: { name: string }) => o.name)).toEqual(['Council tax']);

  // The download is logged for Admins to see (BUD-141).
  await expect(page.getByTestId('downloads-none')).toHaveCount(0);
  const logged = page.getByTestId('download');
  await expect(logged).toHaveCount(1);
  await expect(logged.getByTestId('download-who')).toHaveText('You');
  await expect(logged).toContainText('JSON, current items');
});

test('a downloaded file can be checked before importing', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');
  await page.goto('/household/data');
  await settled(page);

  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-json').click()]);
  await page.getByTestId('import-file').setInputFiles((await download.path())!);

  await expect(page.getByTestId('import-ok')).toBeVisible();
  await expect(page.getByTestId('import-household')).toHaveText('My Household (GBP)');
  await expect(page.getByTestId('import-counts')).toContainText('1 outgoing');

  await page.getByTestId('import-file').setInputFiles({ name: 'other.json', mimeType: 'application/json', buffer: Buffer.from('{"hello":"world"}') });
  await expect(page.getByTestId('import-error')).toHaveText("This isn't a Home Budget export file.");
});

test('a downloaded file can be imported as a new household', async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');
  await page.goto('/household/data');
  await settled(page);

  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-json').click()]);
  await page.getByTestId('import-file').setInputFiles((await download.path())!);
  await expect(page.getByTestId('import-ok')).toBeVisible();

  await page.getByTestId('import-name').fill('Restored copy');
  await page.getByTestId('import-new-household').click();

  await expect(page.getByTestId('imported')).toContainText('Restored copy');
  await expect(page.getByTestId('appbar-household')).toHaveText('Restored copy');
  await page.goto('/outgoings');
  await expect(page.getByTestId('outgoing-name')).toHaveText(['Council tax']);
});

test("a household's data can be replaced from a backup", async ({ page }) => {
  await signIn(page);
  await addOutgoing(page, 'Council tax', 'Housing', '150');
  await page.goto('/household/data');
  await settled(page);
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-json').click()]);
  const backup = (await download.path())!;

  await addOutgoing(page, 'Added after the backup', 'Leisure & memberships', '20');

  await page.goto('/household/data');
  await settled(page);
  await page.getByTestId('import-file').setInputFiles(backup);
  await expect(page.getByTestId('replace-deletes')).toContainText('2 outgoings');
  await expect(page.getByTestId('replace-data')).toBeDisabled();
  await page.getByTestId('replace-confirm').fill('My Household');
  await page.getByTestId('replace-data').click();

  await expect(page.getByTestId('replaced')).toBeVisible();
  await page.goto('/outgoings');
  await expect(page.getByTestId('outgoing-name')).toHaveText(['Council tax']);
});

test('an admin can download the household as CSV files for Excel', async ({ page }) => {
  await signIn(page);
  await page.goto('/household/data');
  await settled(page);

  const [download] = await Promise.all([page.waitForEvent('download'), page.getByTestId('download-csv').click()]);

  expect(download.suggestedFilename()).toMatch(/^home-budget-my-household-\d{4}-\d{2}-\d{2}-csv\.zip$/);
  const bytes = await readFile((await download.path())!);
  expect(bytes.subarray(0, 2).toString('latin1')).toBe('PK');
});
