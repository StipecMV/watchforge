import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsService } from './settings.service';
import { API_BASE_URL } from './api.service';

describe('SettingsService (S6-5)', () => {
  let service: SettingsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SettingsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('updateMe PUTuje /api/v1/auth/me s avatarId a locale', () => {
    service.updateMe(7, 'en').subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/auth/me`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({ avatarId: 7, locale: 'en' });
    req.flush({ avatarId: 7, locale: 'en' });
  });

  it('changePassword POSTuje /api/v1/auth/change-password', () => {
    service.changePassword('stare', 'nove-heslo-123').subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/auth/change-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ currentPassword: 'stare', newPassword: 'nove-heslo-123' });
    req.flush({ ok: true });
  });

  it('updateCamera PUTuje /api/v1/cameras/{id}', () => {
    service.updateCamera(3, 'Predný dvor', 'yard', false).subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/cameras/3`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ friendlyName: 'Predný dvor', iconId: 'yard', isActive: false });
    req.flush({ cameraId: 3, friendlyName: 'Predný dvor', iconId: 'yard', isActive: false });
  });

  it('getProfile GETuje /api/v1/profiles?cameraId=N', () => {
    service.getProfile(2).subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/profiles?cameraId=2`);
    expect(req.request.method).toBe('GET');
    req.flush({ configVersionId: 1, cameraId: 2, profileType: 'per_camera', sensitivity: 0.5, intensityThreshold: 0.03, minContourArea: 0.001, ignoreZones: [], focusZones: [] });
  });

  it('putProfile PUTuje /api/v1/profiles/{cameraId} s requestom', () => {
    service
      .putProfile(2, {
        sensitivity: 0.6,
        intensityThreshold: 0.05,
        minContourArea: 0.002,
        ignoreZones: [{ x: 0.1, y: 0.1, w: 0.2, h: 0.2 }],
        focusZones: [],
      })
      .subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/profiles/2`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      sensitivity: 0.6,
      intensityThreshold: 0.05,
      minContourArea: 0.002,
      ignoreZones: [{ x: 0.1, y: 0.1, w: 0.2, h: 0.2 }],
      focusZones: [],
    });
    req.flush({ configVersionId: 2, cameraId: 2, profileType: 'per_camera', sensitivity: 0.6, intensityThreshold: 0.05, minContourArea: 0.002, ignoreZones: [], focusZones: [] });
  });

  it('getUsers GETuje /api/v1/users (admin)', () => {
    service.getUsers().subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/users`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBe(true);
    req.flush([]);
  });

  it('resetPassword POSTuje /api/v1/auth/reset-password', () => {
    service.resetPassword('user2').subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/auth/reset-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'user2' });
    req.flush({ ok: true });
  });

  it('getSystemInfo GETuje /api/v1/system/info (admin)', () => {
    service.getSystemInfo().subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/system/info`);
    expect(req.request.method).toBe('GET');
    req.flush({
      nvr: { nvrId: 1, siteId: 'site-a', host: '192.168.68.10', port: 34567, username: 'nvr-user', passwordSecretEnv: 'WF_TEST_NVR_PASSWORD' },
      worker: { queued: 0, running: 0, completed: 10, failed: 0, interrupted: 0, cancelled: 0, total: 10 },
      apiVersion: '1.0.0',
      serverTimeUtc: '2026-08-08T12:00:00Z',
    });
  });
});
