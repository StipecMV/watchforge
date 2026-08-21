import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AnnotationDto,
  AnnotationRequest,
  CameraDto,
  CreateRequestDto,
  DetectionDto,
  DetectionListDto,
  RecordingDto,
  RequestStatusDto,
  SystemStatusDto,
} from '../models/video.model';

/**
 * WatchForge API — relatívna cesta (rovnaký origin ako UI).
 * UI server (deploy/scripts/ui-server.py) proxyuje /api/ → WatchForge API,
 * takže nie je potrebný CORS ani hardcoded host/port.
 * Legacy: http://localhost:5000 (priamy prístup, ak API beží separátne).
 */
export const API_BASE_URL = '';

export interface DetectionQuery {
  cameraId?: number | null;
  from?: string;
  to?: string;
  detectionType?: string | null;
  flag?: string | null;
}

export interface RecordingQuery {
  cameraId?: number | null;
  from?: string;
  to?: string;
  sourceType?: string | null;
}

/**
 * REST klient pre WatchForge API v1 (S6-3, event-first dashboard).
 * Legacy /api/videos endpointy boli nahradené v S5-1 — tento klient
 * používa výhradne /api/v1/* (session cookie cez withCredentials).
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly base = `${API_BASE_URL}/api/v1`;

  constructor(private http: HttpClient) {}

  /** GET /api/v1/cameras — zoznam kamier. */
  getCameras(): Observable<CameraDto[]> {
    return this.http.get<CameraDto[]>(`${this.base}/cameras`, { withCredentials: true });
  }

  /** GET /api/v1/system/status — verejné štatistiky analyz (worker summary + sync backlog). */
  getSystemStatus(): Observable<SystemStatusDto> {
    return this.http.get<SystemStatusDto>(`${this.base}/system/status`, { withCredentials: true });
  }

  /** GET /api/v1/detections — detekcie s voliteľnými filtrami ({total, items} — items max 2000). */
  getDetections(query: DetectionQuery = {}): Observable<DetectionListDto> {
    return this.http.get<DetectionListDto>(`${this.base}/detections`, {
      params: this.toParams(query),
      withCredentials: true,
    });
  }

  /** GET /api/v1/recordings — záznamy (segmenty) s voliteľnými filtrami. */
  getRecordings(query: RecordingQuery = {}): Observable<RecordingDto[]> {
    return this.http.get<RecordingDto[]>(`${this.base}/recordings`, {
      params: this.toParams(query),
      withCredentials: true,
    });
  }

  /** POST /api/v1/requests — vytvorí interaktívnu požiadavku (prioritné joby). */
  createRequest(dto: CreateRequestDto): Observable<RequestStatusDto> {
    return this.http.post<RequestStatusDto>(`${this.base}/requests`, dto, {
      withCredentials: true,
    });
  }

  /** GET /api/v1/requests/{id} — stav požiadavky + clipIds. */
  getRequestStatus(id: number): Observable<RequestStatusDto> {
    return this.http.get<RequestStatusDto>(`${this.base}/requests/${id}`, {
      withCredentials: true,
    });
  }

  /** GET /api/v1/clips/{id} — streamovacia URL klipu (MP4 video alebo JPG fotka). */
  clipUrl(id: number): string {
    return `${this.base}/clips/${id}`;
  }

  /** S22l: GET /api/v1/recordings/{id}/video — streamovacia URL LOKÁLNEHO videa nahrávky. */
  recordingVideoUrl(id: number): string {
    return `${this.base}/recordings/${id}/video`;
  }

  /** S20: POST /api/v1/exports — export vybranej časovej úsečky (dôkazový klip). */
  createExport(dto: CreateRequestDto): Observable<RequestStatusDto> {
    return this.http.post<RequestStatusDto>(`${this.base}/exports`, dto, {
      withCredentials: true,
    });
  }

  // ── flag screen (S6-7, FR-16) ─────────────────────────────────────────

  /** GET /api/v1/detections/{id}/annotations — užívateľské anotácie detekcie. */
  getAnnotations(detectionId: number): Observable<AnnotationDto[]> {
    return this.http.get<AnnotationDto[]>(`${this.base}/detections/${detectionId}/annotations`, {
      withCredentials: true,
    });
  }

  /** POST /api/v1/detections/{id}/annotations — nová užívateľská anotácia. */
  addAnnotation(detectionId: number, request: AnnotationRequest): Observable<AnnotationDto> {
    return this.http.post<AnnotationDto>(`${this.base}/detections/${detectionId}/annotations`, request, {
      withCredentials: true,
    });
  }

  /** PUT /api/v1/detections/{id}/annotations/{annotationId} — úprava vlastnej anotácie. */
  updateAnnotation(detectionId: number, annotationId: number, request: AnnotationRequest): Observable<AnnotationDto> {
    return this.http.put<AnnotationDto>(
      `${this.base}/detections/${detectionId}/annotations/${annotationId}`,
      request,
      { withCredentials: true },
    );
  }

  /** DELETE /api/v1/detections/{id}/annotations — „Clear my drawings" (len moje anotácie). */
  clearMyAnnotations(detectionId: number): Observable<{ ok: boolean; detectionId: number; removed: number }> {
    return this.http.delete<{ ok: boolean; detectionId: number; removed: number }>(
      `${this.base}/detections/${detectionId}/annotations`,
      { withCredentials: true },
    );
  }

  /** S22l: POST /api/v1/recordings/{id}/person-reviewed — „skontrolované" (osoba potvrdená). */
  markPersonReviewed(recordingId: number): Observable<{ ok: boolean; recordingId: number; personPending: boolean }> {
    return this.http.post<{ ok: boolean; recordingId: number; personPending: boolean }>(
      `${this.base}/recordings/${recordingId}/person-reviewed`,
      {},
      { withCredentials: true },
    );
  }

  /** POST /api/v1/detections/{id}/flag — flagged / false_positive / none (FR-16). */
  setFlag(detectionId: number, flag: string): Observable<{ ok: boolean; detectionId: number; flag: string }> {
    return this.http.post<{ ok: boolean; detectionId: number; flag: string }>(
      `${this.base}/detections/${detectionId}/flag`,
      { flag },
      { withCredentials: true },
    );
  }

  /** Vytvorí HttpParams z objektu, null/undefined hodnoty vynechá. */
  private toParams(query: object): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value === null || value === undefined || value === '') continue;
      params = params.set(key, String(value));
    }
    return params;
  }
}
