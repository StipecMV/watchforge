import { test, expect } from '@playwright/test';

/**
 * S11-3: E2E flag flow — flag je ADMIN-only operácia (API 403 pre neadminov),
 * preto sa test prihlási ako admin cez #/admin a prejde na analýzy.
 */
test.describe('flag flow (admin)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/#/admin');
    await page.locator('input[autocomplete="username"]').fill('admin');
    await page.locator('input[autocomplete="current-password"]').fill('heslo1234');
    await page.locator('button.primary-cta').click();
    // Live view po prihlásení → prepnúť na Analýzy
    await expect(page.locator('.live-view')).toBeVisible();
    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });
  });

  test('event from seed appears in dashboard list', async ({ page }) => {
    const eventRow = page.locator('.event-row').first();
    await expect(eventRow).toBeVisible({ timeout: 15_000 });
  });

  test('select event → open flag screen → choose flag → save', async ({ page }) => {
    const eventRow = page.locator('.event-row').first();
    await expect(eventRow).toBeVisible({ timeout: 15_000 });
    await eventRow.click();

    await page.locator('.flag-btn').click();
    await expect(page.locator('.flag-screen')).toBeVisible({ timeout: 10_000 });

    await page.locator('.choice-btn').first().click();
    await expect(page.locator('.choice-btn').first()).toHaveClass(/on/);
    await page.locator('.save-btn').click();

    await expect(page.locator('.flag-screen')).not.toBeVisible({ timeout: 10_000 });
    await expect(page.locator('.dash')).toBeVisible();
  });

  test('flag screen cancel returns to dashboard', async ({ page }) => {
    const eventRow = page.locator('.event-row').first();
    await expect(eventRow).toBeVisible({ timeout: 15_000 });
    await eventRow.click();
    await page.locator('.flag-btn').click();
    await expect(page.locator('.flag-screen')).toBeVisible();

    await page.locator('.cancel-btn').click();
    await expect(page.locator('.flag-screen')).not.toBeVisible();
    await expect(page.locator('.dash')).toBeVisible();
  });
});
