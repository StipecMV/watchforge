import { test, expect } from '@playwright/test';

/**
 * S22: Web UI je VEREJNÉ (live view + analýzy bez prihlásenia).
 * Admin sa prihlasuje cez #/admin (meno + heslo) a vidí navyše settings.
 * (Predpokladá seeded DB z scripts/e2e-api.sh: admin/heslo1234.)
 */
test('public UI loads live view without login (no login screen)', async ({ page }) => {
  await page.goto('/');
  // Live view je default (žiadny login screen)
  await expect(page.locator('.live-view')).toBeVisible();
  await expect(page.locator('app-login')).not.toBeVisible();
});

test('admin login via #/admin shows settings button', async ({ page }) => {
  await page.goto('/#/admin');
  await expect(page.locator('input[autocomplete="username"]')).toBeVisible();
  await page.locator('input[autocomplete="username"]').fill('admin');
  await page.locator('input[autocomplete="current-password"]').fill('heslo1234');
  await page.locator('button.primary-cta').click();

  // Po prihlásení: live view + ⚙ settings (admin-only)
  await expect(page.locator('.live-view')).toBeVisible();
  await expect(page.locator('.settings-btn, .admin-btn').first()).toBeVisible();
});

test('admin login with wrong password shows an error', async ({ page }) => {
  await page.goto('/#/admin');
  await page.locator('input[autocomplete="username"]').fill('admin');
  await page.locator('input[autocomplete="current-password"]').fill('nepravne-heslo');
  await page.locator('button.primary-cta').click();

  await expect(page.locator('[role="alert"]')).toBeVisible({ timeout: 10_000 });
  await expect(page.locator('input[autocomplete="username"]')).toBeVisible();
});

test('non-admin does not see settings button', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.live-view')).toBeVisible();
  // Bez prihlásenia — žiadne ⚙ settings
  await expect(page.locator('.settings-btn')).not.toBeVisible();
});

test('analyses (dashboard) open without login', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.live-view')).toBeVisible();
  // Prepnutie na Analýzy cez bottom nav / live analyses button
  const analysesBtn = page.locator('button:has-text("Analýzy")').first();
  await analysesBtn.click();
  await expect(page.locator('app-dashboard')).toBeVisible();
});
