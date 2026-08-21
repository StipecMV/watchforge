import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api.service';
import {
  DetectionProfile,
  DetectionProfileRequest,
  IdentityInfo,
  SettingsUser,
  SystemInfo,
  SystemStatus,
} from '../models/settings.model';

/**
 * REST klient pre Settings obrazovku (S6-5, FR-11/FR-12).
 *
 * Endpointy:
 * - Profile:   PUT /api/v1/auth/me (avatar, locale)
 * - Cameras:   PUT /api/v1/cameras/{id} (friendly name, ikona, active)
 * - Detection: GET /api/v1/profiles?cameraId=N + PUT /api/v1/profiles/{cameraId}
 * - Users:     GET /api/v1/users (admin) + POST /api/v1/auth/reset-password (admin)
 * - Security:  POST /api/v1/auth/change-password
 * - System:    GET /api/v1/system/info (admin)
 * Všetky volania so session cookie (withCredentials).
 */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly base = `${API_BASE_URL}/api/v1`;

  constructor(private http: HttpClient) {}

  /** PUT /api/v1/auth/me — úprava vlastného profilu (avatar 1..10, locale sk|en). */
  updateMe(avatarId: number, locale: string): Observable<{ avatarId: number; locale: string }> {
    return this.http.put<{ avatarId: number; locale: string }>(
      `${this.base}/auth/me`,
      { avatarId, locale },
      { withCredentials: true },
    );
  }

  /** POST /api/v1/auth/change-password — zmena hesla (aktuálne + nové min 8). */
  changePassword(currentPassword: string, newPassword: string): Observable<{ ok: boolean }> {
    return this.http.post<{ ok: boolean }>(
      `${this.base}/auth/change-password`,
      { currentPassword, newPassword },
      { withCredentials: true },
    );
  }

  /** PUT /api/v1/cameras/{id} — úprava profilu kamery (friendly name, ikona, active). */
  updateCamera(
    cameraId: number,
    friendlyName: string,
    iconId: string,
    isActive: boolean,
  ): Observable<{ cameraId: number; friendlyName: string; iconId: string; isActive: boolean }> {
    return this.http.put<{ cameraId: number; friendlyName: string; iconId: string; isActive: boolean }>(
      `${this.base}/cameras/${cameraId}`,
      { friendlyName, iconId, isActive },
      { withCredentials: true },
    );
  }

  /** GET /api/v1/profiles?cameraId=N — aktívny detekčný profil (fallback shared). */
  getProfile(cameraId: number): Observable<DetectionProfile> {
    return this.http.get<DetectionProfile>(`${this.base}/profiles`, {
      params: { cameraId },
      withCredentials: true,
    });
  }

  /** PUT /api/v1/profiles/{cameraId} — nová verzia per-camera profilu (pre nové analýzy). */
  putProfile(cameraId: number, request: DetectionProfileRequest): Observable<DetectionProfile> {
    return this.http.put<DetectionProfile>(`${this.base}/profiles/${cameraId}`, request, {
      withCredentials: true,
    });
  }

  /** GET /api/v1/profiles/shared — zdieľaný profil (platí pre všetky kamery). */
  getSharedProfile(): Observable<DetectionProfile> {
    return this.http.get<DetectionProfile>(`${this.base}/profiles/shared`, { withCredentials: true });
  }

  /** PUT /api/v1/profiles/shared — nová verzia zdieľaného profilu. */
  putSharedProfile(request: DetectionProfileRequest): Observable<DetectionProfile> {
    return this.http.put<DetectionProfile>(`${this.base}/profiles/shared`, request, { withCredentials: true });
  }

  /** GET /api/v1/users — zoznam používateľov (admin). */
  getUsers(): Observable<SettingsUser[]> {
    return this.http.get<SettingsUser[]>(`${this.base}/users`, { withCredentials: true });
  }

  /** POST /api/v1/auth/reset-password — admin reset hesla (vymaže hash → nové pri logine). */
  resetPassword(username: string): Observable<{ ok: boolean }> {
    return this.http.post<{ ok: boolean }>(
      `${this.base}/auth/reset-password`,
      { username },
      { withCredentials: true },
    );
  }

  /** GET /api/v1/system/info — System sekcia (admin): NVR + worker + verzia. */
  getSystemInfo(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>(`${this.base}/system/info`, { withCredentials: true });
  }

  /** S12-2: GET /api/v1/system/status — NVR + worker + sync/backlog (admin). */
  getSystemStatus(): Observable<SystemStatus> {
    return this.http.get<SystemStatus>(`${this.base}/system/status`, { withCredentials: true });
  }

  // ── Identities (S10-5, FR-05) ─────────────────────────────────────────

  getIdentities(): Observable<IdentityInfo[]> {
    return this.http.get<IdentityInfo[]>(`${this.base}/identities`, { withCredentials: true });
  }

  createIdentity(name: string): Observable<IdentityInfo> {
    return this.http.post<IdentityInfo>(`${this.base}/identities`, { name }, { withCredentials: true });
  }

  learnIdentity(detectionId: number, identityId: number): Observable<{ assigned: number }> {
    return this.http.post<{ assigned: number }>(
      `${this.base}/identities/learn`,
      { detectionId, identityId },
      { withCredentials: true },
    );
  }

  deleteIdentity(identityId: number): Observable<{ deleted: boolean }> {
    return this.http.delete<{ deleted: boolean }>(`${this.base}/identities/${identityId}`, {
      withCredentials: true,
    });
  }
}
