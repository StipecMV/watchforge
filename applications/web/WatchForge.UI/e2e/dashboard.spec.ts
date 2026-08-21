import { test, expect } from '@playwright/test';

/**
 * S11-2: E2E verejný dashboard (analýzy) — bez prihlásenia.
 * Overuje: live view default, prepnutie na analýzy, kamery, panely.
 * (Seeded DB: admin/heslo1234, 2 kamery Dvor/Brana.)
 */
test.describe('public dashboard flow', () => {
  test('live view is default; analyses open via Analýzy button', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('.live-view')).toBeVisible();

    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });
  });

  test('dashboard shows camera chip and wordmark', async ({ page }) => {
    await page.goto('/');
    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });

    await expect(page.locator('.dash .wordmark')).toBeVisible();
    // Kamera zo seed DB (Dvor · CH1) — chip v topbare
    await expect(page.locator('.camera-chip').first()).toBeVisible();
  });

  test('panel toggles switch event/video panels', async ({ page }) => {
    await page.goto('/');
    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });

    const pills = page.locator('.panel-toggles .pill');
    await expect(pills).toHaveCount(4);
    for (let i = 0; i < 4; i++) {
      await expect(pills.nth(i)).toHaveClass(/on/);
    }
    await pills.nth(0).click();
    await expect(pills.nth(0)).not.toHaveClass(/on/);
    await expect(pills.nth(1)).toHaveClass(/on/);
  });

  test('settings button hidden for non-admin (public UI)', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('.live-view')).toBeVisible();
    await expect(page.locator('.settings-btn')).not.toBeVisible();
  });
});
