import { test as base } from '@playwright/test';

export { expect } from '@playwright/test';

/**
 * The standard test, plus: when a test fails, print what the browser reported (console errors,
 * uncaught exceptions, failed requests) so the CI log explains the failure on its own.
 */
export const test = base.extend<{ browserDiagnostics: void }>({
  browserDiagnostics: [
    async ({ page }, use, testInfo) => {
      const lines: string[] = [];
      page.on('console', message => {
        if (message.type() === 'error' || message.type() === 'warning') {
          lines.push(`console.${message.type()}: ${message.text()}`);
        }
      });
      page.on('pageerror', error => lines.push(`page error: ${error.message}`));
      page.on('requestfailed', request => lines.push(`request failed: ${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`));
      page.on('response', response => {
        if (response.status() >= 400) {
          lines.push(`HTTP ${response.status()}: ${response.request().method()} ${response.url()}`);
        }
      });

      await use();

      if (testInfo.status !== testInfo.expectedStatus) {
        console.log(`\n--- Browser diagnostics for "${testInfo.title}" [${testInfo.project.name}] ---\n${lines.slice(0, 40).join('\n') || '(none)'}\nURL: ${page.url()}\nBody: ${(await page.locator('body').innerText().catch(() => '')).slice(0, 500)}\n---`);
      }
    },
    { auto: true },
  ],
});
