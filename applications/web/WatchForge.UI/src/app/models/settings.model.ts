/**
 * WatchForge UI — Settings modely (S6-5).
 *
 * Zrkadlia DTO z WatchForge.Api (camelCase, časy UTC ISO 8601).
 * Heslo NIKDY neprichádza z API — System sekcia ukazuje len passwordSecretEnv
 * (názov env secretu, NFR-05).
 */

/** Používateľ v Settings → Users (admin). */
export interface SettingsUser {
  userId: number;
  username: string;
  role: string; // admin | standard
  avatarId: number; // 1..10
  locale: string; // sk | en
  hasPassword: boolean;
}

/** NVR pripojenie v System sekcii (bez hesla). */
export interface NvrInfo {
  nvrId: number;
  siteId: string;
  host: string;
  port: number;
  username: string;
  passwordSecretEnv: string;
}

/** Worker job summary (z JOBS tabuľky — /internal/jobs čítanie cez API). */
export interface WorkerInfo {
  queued: number;
  running: number;
  completed: number;
  failed: number;
  interrupted: number;
  cancelled: number;
  total: number;
}

/** System sekcia: NVR + worker + verzia API + služby (S22g). */
export interface SystemInfo {
  nvr: NvrInfo;
  worker: WorkerInfo;
  apiVersion: string;
  serverTimeUtc: string;
  services: ServiceInfo[];
}

/** Jedna služba WatchForge (api/runner/web) — stav + verzia + port. */
export interface ServiceInfo {
  name: string;
  status: string;
  version: string;
  port: number;
}

/** S12-2: stav synchronizácie/backlogu (GET /api/v1/system/status). */
export interface SystemStatus {
  nvr: NvrInfo;
  worker: WorkerInfo;
  sync: {
    backlog: number;
    lastSyncUtc: string | null;
    totalRecordings: number;
    completedAnalyses: number;
  };
  apiVersion: string;
  serverTimeUtc: string;
}

/**
 * Zóna profilu (IGNORE/FOCUS), normalizovaná 0..1 — 4K frame (S6-6).
 * X/Y/W/H = ohraničujúci obdĺžnik (bbox), ktorý analýza aplikuje;
 * shape/points = nakreslený tvar (len polygon/freehand, pre budúce použitie).
 */
export interface ZoneDto {
  x: number;
  y: number;
  w: number;
  h: number;
  name?: string;
  shape?: 'rect' | 'polygon' | 'freehand';
  points?: number[][];
}

/** Aktívny detekčný profil kamery (GET /api/v1/profiles). */
export interface DetectionProfile {
  configVersionId: number;
  cameraId: number | null;
  profileType: string; // per_camera | shared
  sensitivity: number; // 0..1
  intensityThreshold: number; // 0..1
  minContourArea: number; // 0..1 (normalizovaná plocha)
  ignoreZones: ZoneDto[];
  focusZones: ZoneDto[];
}

/** Request pre PUT /api/v1/profiles/{cameraId}. */
export interface DetectionProfileRequest {
  sensitivity: number;
  intensityThreshold: number;
  minContourArea: number;
  ignoreZones: ZoneDto[];
  focusZones: ZoneDto[];
}

/** Identita človeka (S10-5, GET /api/v1/identities). */
export interface IdentityInfo {
  identityId: number;
  name: string;
  createdAt: string;
  faceCount: number;
}
