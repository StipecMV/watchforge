import { test, expect } from '@playwright/test';

/**
 * S11-4: E2E settings + témy — settings je ADMIN-only (prihlásenie cez #/admin).
 */
test.describe('settings and themes (admin)', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/#/admin');
    await page.locator('input[autocomplete="username"]').fill('admin');
    await page.locator('input[autocomplete="current-password"]').fill('heslo1234');
    await page.locator('button.primary-cta').click();
    await expect(page.locator('.live-view')).toBeVisible();
  });

  test('settings opens with nav sections and identity section works', async ({ page }) => {
    // Admin: ⚙ v sidebar (admin-btn) alebo cez dashboard — otvoríme cez Analýzy
    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });
    await page.locator('.admin-settings').click();
    await expect(page.locator('.settings .badge')).toHaveText('SETTINGS');

    const nav = page.locator('.nav');
    await expect(nav.getByText('Profil')).toBeVisible();
    await expect(nav.getByText('Identity')).toBeVisible();

    await nav.getByText('Identity').click();
    await expect(page.getByText('Zatiaľ žiadne identity')).toBeVisible();
  });

  test('settings dashboard button returns to dashboard', async ({ page }) => {
    const analysesBtn = page.locator('button:has-text("Analýzy")').first();
    await analysesBtn.click();
    await expect(page.locator('app-dashboard')).toBeVisible({ timeout: 15_000 });
    await page.locator('.admin-settings').click();
    await page.locator('.dashboard-btn').click();
    await expect(page.locator('.dash')).toBeVisible();
  });

  test('theme switcher changes active theme', async ({ page }) => {
    const dots = page.locator('.theme-dot');
    await expect(dots).toHaveCount(3);

    const before = await page.locator('.theme-dot.active').getAttribute('data-theme-id');
    const target = before === 'light-green' ? 'dark-purple' : 'light-green';
    await page.locator(`.theme-dot[data-theme-id="${target}"]`).click();
    await expect(page.locator(`.theme-dot[data-theme-id="${target}"]`)).toHaveClass(/active/);
  });
});
