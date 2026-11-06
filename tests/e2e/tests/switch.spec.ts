import { expect, test } from './fixtures';
import { settled, signIn } from './helpers';

// BUD-60: the 44px touch-target rule stretched the switch's thumb, so it sat at the top of its track
// instead of in the middle.
test('a switch thumb is centred on its track', async ({ page }) => {
  await signIn(page);
  await page.goto('/outgoings/new');
  await settled(page);

  const toggle = page.locator('.mud-switch').filter({ hasText: 'It renews' });
  await expect(toggle).toBeVisible();

  for (const checked of [false, true]) {
    const measured = await toggle.evaluate(element => {
      const centre = (el: Element | null) => {
        if (!el) return null;
        const box = el.getBoundingClientRect();
        return box.top + box.height / 2;
      };
      const thumb = element.querySelector('[class*="mud-switch-thumb"]');
      const track = element.querySelector('.mud-switch-track');
      return { thumb: centre(thumb), track: centre(track), html: element.outerHTML.slice(0, 1500) };
    });

    expect(measured.thumb, `no thumb found in ${measured.html}`).not.toBeNull();
    expect(measured.track, `no track found in ${measured.html}`).not.toBeNull();
    expect(Math.abs(measured.thumb! - measured.track!), `thumb vs track centres (checked=${checked}) in ${measured.html}`).toBeLessThanOrEqual(2);

    if (!checked) {
      await toggle.click();
      await expect(page.getByTestId('renewal-date')).toBeVisible();
    }
  }
});
