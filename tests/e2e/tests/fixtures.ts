import { expect as baseExpect, test as base } from '@playwright/test';

export { expect } from '@playwright/test';

/**
 * The standard test, plus: when a test fails, print what the browser reported (console errors,
 * uncaught exceptions, failed requests) so the CI log explains the failure on its own. Every test also fails if the
 * browser blocked anything under the Content Security Policy (BUD-143), so the whole suite checks the policy.
 */
export const test = base.extend<{ browserDiagnostics: void }>({
  browserDiagnostics: [
    async ({ page }, use, testInfo) => {
      const lines: string[] = [];
      const blocked: string[] = [];
      page.on('console', message => {
        if (message.text().includes('Content Security Policy')) {
          blocked.push(message.text());
        }
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

      baseExpect(blocked, 'The Content Security Policy blocked something').toEqual([]);
    },
    { auto: true },
  ],
});
