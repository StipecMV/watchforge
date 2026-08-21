import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService, API_BASE_URL } from './api.service';

describe('ApiService (S6-3, /api/v1)', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('getCameras volá GET /api/v1/cameras s credentials', () => {
    let result: unknown;
    api.getCameras().subscribe(r => (result = r));

    const req = http.expectOne(`${API_BASE_URL}/api/v1/cameras`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBe(true);
    req.flush([{ cameraId: 1, friendlyName: 'Dvor' }]);
    expect(result).toEqual([{ cameraId: 1, friendlyName: 'Dvor' }]);
  });

  it('getDetections posiela query filtre a null filtre vynechá', () => {
    api.getDetections({ cameraId: 2, from: '2026-08-08T00:00:00Z', to: '2026-08-08T23:59:59Z' }).subscribe();

    const req = http.expectOne(
      `${API_BASE_URL}/api/v1/detections?cameraId=2&from=2026-08-08T00:00:00Z&to=2026-08-08T23:59:59Z`,
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('getDetections prenesie detectionType a flag filtre', () => {
    api
      .getDetections({ cameraId: 1, detectionType: 'person', flag: 'flagged' })
      .subscribe();

    const req = http.expectOne(
      `${API_BASE_URL}/api/v1/detections?cameraId=1&detectionType=person&flag=flagged`,
    );
    req.flush([]);
  });

  it('getRecordings volá GET /api/v1/recordings s filtrami', () => {
    api.getRecordings({ cameraId: 3, from: '2026-08-08T00:00:00Z' }).subscribe();

    const req = http.expectOne(
      `${API_BASE_URL}/api/v1/recordings?cameraId=3&from=2026-08-08T00:00:00Z`,
    );
    expect(req.request.withCredentials).toBe(true);
    req.flush([]);
  });

  it('createRequest POSTuje na /api/v1/requests a vráti status', () => {
    let result: unknown;
    api
      .createRequest({
        fromTime: '2026-08-08T14:00:00Z',
        toTime: '2026-08-08T14:00:30Z',
        cameraId: 1,
      })
      .subscribe(r => (result = r));

    const req = http.expectOne(`${API_BASE_URL}/api/v1/requests`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({
      fromTime: '2026-08-08T14:00:00Z',
      toTime: '2026-08-08T14:00:30Z',
      cameraId: 1,
    });
    req.flush({ requestId: 7, status: 'queued', estimate: '~2 min', clipIds: [], error: null });
    expect(result).toEqual({ requestId: 7, status: 'queued', estimate: '~2 min', clipIds: [], error: null });
  });

  it('getRequestStatus volá GET /api/v1/requests/{id}', () => {
    api.getRequestStatus(7).subscribe();

    const req = http.expectOne(`${API_BASE_URL}/api/v1/requests/7`);
    expect(req.request.method).toBe('GET');
    req.flush({ requestId: 7, status: 'completed', clipIds: [11], error: null });
  });

  it('clipUrl vráti streamovaciu URL klipu', () => {
    expect(api.clipUrl(11)).toBe(`${API_BASE_URL}/api/v1/clips/11`);
  });
});
