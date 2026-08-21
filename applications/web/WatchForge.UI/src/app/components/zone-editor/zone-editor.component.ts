import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { I18nService } from '../../i18n/i18n.service';
import { CameraDto } from '../../models/video.model';
import { DetectionProfile, ZoneDto } from '../../models/settings.model';
import { LocationIconComponent } from '../icons/location-icon.component';

/** Kresliace nástroje (design handoff focus-zone-editor.jsx). */
export type ZoneTool = 'rect' | 'polygon' | 'freehand';
/** Typ zóny: IGNORE = maska/exclusion, FOCUS = focus región (FR-06). */
export type ZoneType = 'ignore' | 'focus';

/** Nakreslená zóna v editore — bbox (aplikuje ho analýza) + pôvodný tvar. */
export interface DrawnZone {
  x: number;
  y: number;
  w: number;
  h: number;
  shape: ZoneTool;
  points: number[][];
}

/** Výstup Save: kamera + meno + typ + geometria (SettingsComponent uloží cez PUT profil). */
export interface ZoneSaveEvent {
  cameraId: number;
  name: string;
  type: ZoneType;
  zone: DrawnZone;
}

/** Dizajnový canvas 16:9 (focus-zone-editor.jsx viewBox 1600×900). */
const FRAME_W = 1600;
const FRAME_H = 900;
/** Minimálna plocha zóny (normalizovaná) — ochrana pred nechceným klikom. */
const MIN_EDGE = 0.001;
const EPS = 0.001;

function clamp01(v: number): number {
  return Math.min(1, Math.max(0, v));
}

/**
 * Focus zone editor (S6-6, FR-06, design handoff focus-zone-editor.jsx).
 *
 * Kreslenie zón na canvas (Rectangle drag / Polygon klik / Freehand ťahanie),
 * IGNORE/FOCUS typ, undo/redo/clear, živé štatistiky tvaru, výber kamery.
 * Save emituje `ZoneSaveEvent` — backend (PUT /profiles/{cameraId}) ukladá
 * bbox {x,y,w,h} (analýza aplikuje obdĺžnik) + shape/points pre UI.
 */
@Component({
  selector: 'app-zone-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, LocationIconComponent],
  templateUrl: './zone-editor.component.html',
  styleUrls: ['./zone-editor.component.css'],
})
export class ZoneEditorComponent {
  /** Kamery na výber (Settings → Cameras). */
  @Input() cameras: CameraDto[] = [];
  /** Aktívny profil vybranej kamery — existujúce zóny sa kreslia read-only. */
  @Input() profile: DetectionProfile | null = null;
  /** Edit mode: predvyplní draft, meno a typ. */
  @Input() initialZone: ZoneDto | null = null;
  @Input() initialType: ZoneType = 'ignore';
  /** Save → SettingsComponent uloží zónu do profilu kamery. */
  @Output() saveZone = new EventEmitter<ZoneSaveEvent>();
  @Output() cancel = new EventEmitter<void>();
  /** Zmena kamery v editore → parent načíta profil novej kamery. */
  @Output() cameraChange = new EventEmitter<number>();

  readonly i18n = inject(I18nService);
  readonly FRAME_W = FRAME_W;
  readonly FRAME_H = FRAME_H;
  readonly Math = Math;

  tool: ZoneTool = 'rect';
  zoneType: ZoneType = 'ignore';
  name = '';
  cameraId: number | null = null;

  /** Dokončený tvar (zóna pripravená na save). */
  draft: DrawnZone | null = null;

  // Interný kresliaci stav
  private drawing = false;
  private anchor: { x: number; y: number } | null = null;
  private polyPoints: number[][] = [];
  private freehandPoints: number[][] = [];

  // Undo/redo history (snapshoty draftu vrátane null)
  private history: (DrawnZone | null)[] = [];
  private historyIndex = -1;

  ngOnInit() {
    this.cameraId = this.cameras.find(c => c.isActive)?.cameraId ?? this.cameras[0]?.cameraId ?? null;
    this.zoneType = this.initialType;
    if (this.initialZone) {
      this.draft = {
        x: this.initialZone.x,
        y: this.initialZone.y,
        w: this.initialZone.w,
        h: this.initialZone.h,
        shape: this.initialZone.shape ?? 'rect',
        points: this.initialZone.points ?? this.rectPoints(this.initialZone),
      };
      this.name = this.initialZone.name ?? '';
      this.pushHistory(this.draft);
    }
  }

  // ── nástroje a typ ─────────────────────────────────────────────────────

  setTool(tool: ZoneTool) {
    this.tool = tool;
    // Rozpracovaný polygon sa pri prepnutí nástroja zahodí (draft ostáva).
    this.polyPoints = [];
  }

  /** Nástroje toolbaru (design: Rectangle / Polygon / Freehand). */
  tools(): { id: ZoneTool; label: string }[] {
    return [
      { id: 'rect', label: 'zones.toolRect' },
      { id: 'polygon', label: 'zones.toolPolygon' },
      { id: 'freehand', label: 'zones.toolFreehand' },
    ];
  }

  /** Rozpracované body polygónu (pre template). */
  polyPointsView(): number[][] {
    return this.polyPoints;
  }

  setZoneType(type: ZoneType) {
    this.zoneType = type;
  }

  selectCamera(cameraId: number) {
    this.cameraId = cameraId;
    this.cameraChange.emit(cameraId);
  }

  // ── kreslenie (pointer na SVG canvas) ──────────────────────────────────

  onPointerDown(event: MouseEvent) {
    const p = this.pointFromEvent(event);
    if (this.tool === 'rect') {
      this.drawing = true;
      this.anchor = p;
      this.draft = this.bboxDraft(this.anchor, p);
    } else if (this.tool === 'freehand') {
      this.drawing = true;
      this.freehandPoints = [[p.x, p.y]];
      this.draft = this.shapeDraft('freehand', this.freehandPoints);
    }
  }

  onPointerMove(event: MouseEvent) {
    if (!this.drawing) return;
    const p = this.pointFromEvent(event);
    if (this.tool === 'rect' && this.anchor) {
      this.draft = this.bboxDraft(this.anchor, p);
    } else if (this.tool === 'freehand') {
      this.appendPoint(this.freehandPoints, p);
      this.draft = this.shapeDraft('freehand', this.freehandPoints);
    }
  }

  onPointerUp(event: MouseEvent) {
    if (!this.drawing) return;
    const p = this.pointFromEvent(event);
    if (this.tool === 'rect' && this.anchor) {
      this.draft = this.bboxDraft(this.anchor, p);
      this.pushHistory(this.draft);
    } else if (this.tool === 'freehand') {
      this.appendPoint(this.freehandPoints, p);
      this.draft = this.shapeDraft('freehand', this.freehandPoints);
      this.pushHistory(this.draft);
    }
    this.drawing = false;
    this.anchor = null;
  }

  onSvgClick(event: MouseEvent) {
    if (this.tool !== 'polygon') return;
    this.addPolyPoint(this.pointFromEvent(event));
  }

  onSvgDblClick(event: MouseEvent) {
    if (this.tool !== 'polygon') return;
    this.addPolyPoint(this.pointFromEvent(event));
    this.finishPolygon();
  }

  onSvgKeyDown(event: KeyboardEvent) {
    if (this.tool === 'polygon' && event.key === 'Enter') this.finishPolygon();
  }

  private addPolyPoint(p: { x: number; y: number }) {
    const last = this.polyPoints[this.polyPoints.length - 1];
    if (last && Math.abs(last[0] - p.x) < EPS && Math.abs(last[1] - p.y) < EPS) return;
    this.polyPoints.push([p.x, p.y]);
  }

  private finishPolygon() {
    if (this.polyPoints.length === 0) return;
    this.draft = this.shapeDraft('polygon', this.polyPoints);
    this.pushHistory(this.draft);
    this.polyPoints = [];
  }

  /** clientX/Y → normalizované 0..1 (canvas meranie z getBoundingClientRect). */
  private pointFromEvent(event: MouseEvent): { x: number; y: number } {
    const svg = (event.currentTarget as Element | null)?.querySelector?.('svg') as SVGSVGElement | null
      ?? (event.target as SVGSVGElement);
    const rect = svg.getBoundingClientRect();
    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height),
    };
  }

  private appendPoint(list: number[][], p: { x: number; y: number }) {
    const last = list[list.length - 1];
    if (last && Math.abs(last[0] - p.x) < EPS && Math.abs(last[1] - p.y) < EPS) return;
    list.push([p.x, p.y]);
  }

  private bboxDraft(a: { x: number; y: number }, b: { x: number; y: number }): DrawnZone {
    const x = Math.min(a.x, b.x);
    const y = Math.min(a.y, b.y);
    const w = Math.abs(b.x - a.x);
    const h = Math.abs(b.y - a.y);
    return { x, y, w, h, shape: 'rect', points: this.rectPoints({ x, y, w, h }) };
  }

  private rectPoints(z: { x: number; y: number; w: number; h: number }): number[][] {
    return [[z.x, z.y], [z.x + z.w, z.y], [z.x + z.w, z.y + z.h], [z.x, z.y + z.h]];
  }

  /** bbox z ľubovoľných bodov (polygon/freehand) — analýza aplikuje bbox. */
  private shapeDraft(shape: 'polygon' | 'freehand', points: number[][]): DrawnZone {
    const xs = points.map(p => p[0]);
    const ys = points.map(p => p[1]);
    const minX = Math.min(...xs);
    const maxX = Math.max(...xs);
    const minY = Math.min(...ys);
    const maxY = Math.max(...ys);
    return {
      x: minX, y: minY, w: maxX - minX, h: maxY - minY,
      shape,
      points: points.map(p => [p[0], p[1]]),
    };
  }

  // ── undo / redo / clear ────────────────────────────────────────────────

  get canUndo(): boolean {
    return this.historyIndex >= 0;
  }

  get canRedo(): boolean {
    return this.historyIndex < this.history.length - 1;
  }

  undo() {
    if (this.historyIndex < 0) return;
    this.historyIndex--;
    this.draft = this.historyIndex >= 0 ? this.history[this.historyIndex] : null;
  }

  redo() {
    if (this.historyIndex >= this.history.length - 1) return;
    this.historyIndex++;
    this.draft = this.history[this.historyIndex];
  }

  clear() {
    if (this.draft) this.pushHistory(null);
    this.draft = null;
    this.polyPoints = [];
    this.freehandPoints = [];
  }

  private pushHistory(draft: DrawnZone | null) {
    this.history = this.history.slice(0, this.historyIndex + 1);
    this.history.push(draft ? this.cloneZone(draft) : null);
    this.historyIndex++;
  }

  private cloneZone(z: DrawnZone): DrawnZone {
    return { ...z, points: z.points.map(p => [...p]) };
  }

  // ── štatistiky tvaru ───────────────────────────────────────────────────

  get stats() {
    const d = this.draft;
    if (!d) {
      return { hasShape: false, xPx: 0, yPx: 0, wPx: 0, hPx: 0, areaPct: 0, points: 0 };
    }
    return {
      hasShape: true,
      xPx: Math.round(d.x * FRAME_W),
      yPx: Math.round(d.y * FRAME_H),
      wPx: Math.round(d.w * FRAME_W),
      hPx: Math.round(d.h * FRAME_H),
      areaPct: Number((d.w * d.h * 100).toFixed(1)),
      points: d.shape === 'rect' ? 4 : d.points.length,
    };
  }

  // ── save / cancel ──────────────────────────────────────────────────────

  get canSave(): boolean {
    const d = this.draft;
    return d !== null && d.w >= MIN_EDGE && d.h >= MIN_EDGE;
  }

  save() {
    if (!this.canSave || !this.draft) return;
    this.saveZone.emit({
      cameraId: this.cameraId ?? this.cameras[0]?.cameraId ?? 0,
      name: this.name.trim() || this.i18n.translate('zones.defaultName'),
      type: this.zoneType,
      zone: this.cloneZone(this.draft),
    });
  }

  onCancel() {
    this.cancel.emit();
  }

  // ── zobrazenie na canvase ──────────────────────────────────────────────

  /** Existujúce zóny profilu (read-only, dashed) — ignore aj focus. */
  existingZones(): { zone: ZoneDto; type: ZoneType }[] {
    const list: { zone: ZoneDto; type: ZoneType }[] = [];
    for (const z of this.profile?.ignoreZones ?? []) list.push({ zone: z, type: 'ignore' });
    for (const z of this.profile?.focusZones ?? []) list.push({ zone: z, type: 'focus' });
    return list;
  }

  /** SVG body draftu pre <polygon>/<polyline> (súradnice viewBox). */
  draftPointsAttr(): string {
    if (!this.draft || this.draft.shape === 'rect') return '';
    return this.draft.points.map(p => `${(p[0] * FRAME_W).toFixed(1)},${(p[1] * FRAME_H).toFixed(1)}`).join(' ');
  }

  /** SVG body rozpracovaného polygónu (klik po kliku). */
  polyPreviewAttr(): string {
    if (this.tool !== 'polygon' || this.polyPoints.length === 0) return '';
    return this.polyPoints.map(p => `${(p[0] * FRAME_W).toFixed(1)},${(p[1] * FRAME_H).toFixed(1)}`).join(' ');
  }

  zoneColor(type: ZoneType): string {
    return type === 'ignore' ? 'var(--wf-motion, #e25c2a)' : 'var(--wf-brand, #0f8a4a)';
  }

  cameraName(cameraId: number): string {
    return this.cameras.find(c => c.cameraId === cameraId)?.friendlyName ?? '';
  }
}
