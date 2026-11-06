import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

// BUD-59: the 44px touch-target rule made the calendar's days too wide, so weeks wrapped after six days
// and every date sat under the wrong weekday.
test('the date picker shows seven days in every full week', async ({ page }) => {
  await signIn(page);
  await page.goto('/income/new');
  await settled(page);

  await page.getByTestId('starts-on').click();
  const days = page.locator('.mud-picker-calendar-day:visible');
  await expect(days.first()).toBeVisible();

  const rows = await days.evaluateAll(buttons => {
    const byRow = new Map<number, number>();
    for (const button of buttons) {
      if (!/^\d+$/.test((button.textContent ?? '').trim())) continue;
      const top = Math.round(button.getBoundingClientRect().top);
      byRow.set(top, (byRow.get(top) ?? 0) + 1);
    }
    return [...byRow.entries()].sort((a, b) => a[0] - b[0]).map(([, count]) => count);
  });

  // The first and last weeks of a month can be partial; every week in between has all seven days.
  expect(rows.length).toBeGreaterThanOrEqual(4);
  expect(rows.slice(1, -1).every(count => count === 7), `days per row: ${rows.join(', ')}`).toBe(true);
  expect(rows.every(count => count <= 7), `days per row: ${rows.join(', ')}`).toBe(true);
});
