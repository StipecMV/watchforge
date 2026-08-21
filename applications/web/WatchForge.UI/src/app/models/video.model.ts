/**
 * WatchForge UI — dátové modely (S6-3, event-first).
 *
 * Zrkadlia DTO z WatchForge.Contracts.Library (camelCase, časy UTC ISO 8601,
 * regióny normalizované 0..1). Legacy modely (ChannelGroup/VideoItem/
 * ConversionStatus/DetectionsFile) boli odstránené — REST API je /api/v1/*.
 */

export interface CameraDto {
  cameraId: number;
  nvrId: number;
  channel: number;
  friendlyName: string;
  iconId: string;
  isActive: boolean;
}

export interface RecordingDto {
  recordingId: number;
  nvrId: number;
  cameraId: number;
  sourceType: string; // segment | event_clip
  nvrFilename: string;
  beginTime: string; // ISO 8601 UTC
  endTime: string;
  durationSec: number;
  sizeBytes: number;
  codec: string;
  width: number;
  height: number;
  availability: string; // available | unavailable | missed_during_outage
  analysisState: string; // queued|downloading|analyzing|completed|failed|interrupted
  persisted: boolean;
  /** S22l: analýza našla osobu — skip pre cleanup (chránená do potvrdenia). */
  personPending?: boolean;
  /** S22n: začiatok 15-min okna, na ktoré je video trimnuté (player 0 = toto). */
  windowStartUtc?: string;
}

export interface DetectionDto {
  detectionId: number;
  recordingId: number;
  cameraId: number;
  detectionType: string; // motion | person | vehicle | animal | face
  timestampMs: number; // od začiatku segmentu
  durationMs: number;
  confidence: number;
  algorithmVersion: string;
  configVersionId: number;
  regionX: number; // 0..1 (4K frame)
  regionY: number;
  regionW: number;
  regionH: number;
  intensity: number;
  objectClass: string;
  flag: string; // none | flagged | false_positive
}

/** GET /api/v1/detections — total = presný počet, items = max 2000 najnovších. */
export interface DetectionListDto {
  total: number;
  items: DetectionDto[];
}

/** GET /api/v1/system/status — verejné štatistiky analyz. */
export interface SystemStatusDto {
  worker: {
    queued: number;
    running: number;
    completed: number;
    failed: number;
    interrupted: number;
    cancelled: number;
    total: number;
  };
  sync: {
    backlog: number;
    lastSyncUtc: string | null;
    totalRecordings: number;
    completedAnalyses: number;
  };
  apiVersion: string;
  serverTimeUtc: string;
  /** S22j: analýzy zapnuté/vypnuté (WatchForge__Api__Features__AnalysesEnabled). */
  analysesEnabled: boolean;
}

export interface RequestStatusDto {
  requestId: number;
  status: string; // queued|processing|completed|failed|priority_missed
  estimate: string;
  clipIds: number[];
  error: string | null;
}

export interface CreateRequestDto {
  fromTime: string; // ISO 8601 UTC
  toTime: string;
  cameraId: number | null;
  detectionTypeFilter?: string | null;
  contextBeforeSec?: number;
  contextAfterSec?: number;
}

/** Detekčný región normalizovaný 0..1 — pre timeline a player overlay. */
export interface DetectionRegion {
  x: number;
  y: number;
  width: number;
  height: number;
  intensity?: number;
}

/** Udalosť v tvare pre timeline canvas a player overlay (S6-3). */
export interface DetectionEvent {
  timestampMs: number; // relatívne k prehrávanému klipu/segmentu
  durationMs: number;
  regions: DetectionRegion[];
}

/**
 * Udalosť dashboardu: detekcia spojená so záznamom (absolútny čas udalosti
 * = beginTime záznamu + timestampMs detekcie).
 */
export interface DashboardEvent {
  detection: DetectionDto;
  recording: RecordingDto | null;
  absoluteTime: Date;
  event: DetectionEvent;
}

/** Filter chipy dashboardu (FR-11, S6-4): All / Person / Vehicle / High intensity / Flagged. */
export type EventFilter = 'all' | 'person' | 'vehicle' | 'high' | 'flagged';

/** Užívateľská anotácia detekcie (FR-16, S6-7 flag screen) — zelený „USER ·" box. */
export interface AnnotationDto {
  annotationId: number;
  detectionId: number;
  userId: number;
  regionX: number; // 0..1
  regionY: number;
  regionW: number;
  regionH: number;
  label: string;
  createdAt: string; // ISO 8601 UTC
}

/** Telo POST/PUT anotácie (DetectionsController.AnnotationRequest). */
export interface AnnotationRequest {
  regionX: number;
  regionY: number;
  regionW: number;
  regionH: number;
  label?: string;
}

/** Hodnota flagu detekcie (FR-16): none | flagged | false_positive. */
export type FlagValue = 'none' | 'flagged' | 'false_positive';
