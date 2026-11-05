import { expect, test } from './fixtures';
import { signIn } from './helpers';

test('has an installable manifest with icons', async ({ request }) => {
  const response = await request.get('/manifest.webmanifest');
  expect(response.ok()).toBeTruthy();
  const manifest = await response.json();

  expect(manifest.name).toBe('Home Budget');
  expect(manifest.short_name).toBe('Budget');
  expect(manifest.display).toBe('standalone');
  expect(manifest.icons.map((i: { sizes: string; purpose: string }) => `${i.sizes} ${i.purpose}`))
    .toEqual(expect.arrayContaining(['192x192 any', '512x512 any', '512x512 maskable']));

  for (const src of [...manifest.icons.map((i: { src: string }) => i.src), 'icons/apple-touch-icon.png']) {
    const icon = await request.get(`/${src}`);
    expect(icon.ok(), src).toBeTruthy();
    expect(icon.headers()['content-type']).toContain('image/png');
  }
});

test('opens offline, says so, never caches household data, and recovers', async ({ page, context }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', 'Once is enough');
  await signIn(page);

  // The service worker installs on the first visit and controls the page from the next one.
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload();
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
  await page.goto('/income');
  await expect(page.getByRole('heading', { level: 1, name: 'Income' })).toBeVisible();

  const cachedUrls = await page.evaluate(async () => {
    const urls: string[] = [];
    for (const key of await caches.keys()) {
      const cache = await caches.open(key);
      urls.push(...(await cache.keys()).map(r => r.url));
    }
    return urls;
  });
  expect(cachedUrls.length).toBeGreaterThan(10);
  expect(cachedUrls.filter(u => /\/(api|bff|account)\//.test(u))).toEqual([]);

  await context.setOffline(true);
  await page.reload();
  await expect(page.getByTestId('offline-banner')).toBeVisible();

  await context.setOffline(false);
  await expect(page.getByTestId('offline-banner')).toBeHidden();
  await expect(page.getByTestId('appbar-household')).toHaveText('My Household');
});
