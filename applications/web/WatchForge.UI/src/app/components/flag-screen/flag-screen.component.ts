import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { from, concatMap } from 'rxjs';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { I18nService } from '../../i18n/i18n.service';
import { ApiService } from '../../services/api.service';
import { AnnotationRequest, DetectionDto, DetectionRegion } from '../../models/video.model';

/** Nástroje flag screen toolbaru (design handoff flag-screen.jsx). */
export type FlagTool = 'select' | 'rect' | 'text';
/** Hodnota flagu, ktorú ukladá „Save flag" (FR-16). */
export type FlagChoice = 'flagged' | 'false_positive';

/** Užívateľská anotácia v editore — zelený „USER ·" box (FR-16). */
export interface UserAnnotation {
  /** >0 = uložená (annotationId z DB), <0 = lokálna (ešte neuložená). */
  key: number;
  /** 0 = lokálna anotácia (POST pri save). */
  annotationId: number;
  regionX: number; // 0..1
  regionY: number;
  regionW: number;
  regionH: number;
  label: string;
  /** Lokálne zmenená (posun/resize/label) → PUT pri save. */
  dirty: boolean;
}

/** Dizajnový canvas 16:9 (flag-screen.jsx viewBox 1600×900). */
const FRAME_W = 1600;
const FRAME_H = 900;
/** Minimálna veľkosť boxu (normalizovaná) — ochrana pred nechceným klikom. */
const MIN_EDGE = 0.004;
/** Default veľkosť textovej anotácie (text tool). */
const TEXT_W = 0.14;
const TEXT_H = 0.05;

function clamp01(v: number): number {
  return Math.min(1, Math.max(0, v));
}

/**
 * Flag screen (S6-7, FR-11 + FR-16, design handoff flag-screen.jsx).
 *
 * Fullscreen editor nad udalosťou: červené boxy = systémové detekcie
 * („SYSTEM ·"), zelené boxy = užívateľské anotácie („USER ·", resize handles).
 * Nástroje Select / Rectangle / Text label, undo/redo,
 * „Clear my drawings" maže LEN užívateľské anotácie (DELETE pri save).
 * „Save flag" uloží flag (flagged / false_positive) + anotácie
 * (POST nové, PUT zmenené) — anotácie prežijú reload (FR-16 AK).
 */
@Component({
  selector: 'app-flag-screen',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe],
  templateUrl: './flag-screen.component.html',
  styleUrls: ['./flag-screen.component.css'],
})
export class FlagScreenComponent implements OnInit {
  /** Udalosť, ktorá sa flaguje (systémový box + save). */
  @Input() detection: DetectionDto | null = null;
  /** Názov kamery do top baru a REC chipu. */
  @Input() cameraName = '';
  /** Čas udalosti (top bar, napr. „06:14:02") — formátuje dashboard. */
  @Input() eventTime = '';
  /** Dátum + čas (spodný pravý chip, napr. „11 May 2026 · 06:14:02"). */
  @Input() eventDate = '';
  /** Voliteľný background framu (fotka klipu); null = syntetická scéna z handoff. */
  @Input() frameUrl: string | null = null;

  /** Uložené: flag + anotácie. Parent zatvorí flag screen. */
  @Output() saved = new EventEmitter<{ flag: FlagChoice }>();
  @Output() cancel = new EventEmitter<void>();
  @Output() home = new EventEmitter<void>();

  readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);

  readonly FRAME_W = FRAME_W;
  readonly FRAME_H = FRAME_H;

  tool: FlagTool = 'rect';
  flagChoice: FlagChoice = 'flagged';
  /** Používateľ zmenil výber flagu → POST pri save. */
  flagTouched = false;
  /** Užívateľské anotácie (uložené + lokálne). */
  annotations: UserAnnotation[] = [];
  /** Vybraný box (select tool). */
  selectedKey: number | null = null;
  /** Box v režime úpravy labelu (text tool). */
  editingLabelId: number | null = null;
  saving = false;
  saveError = false;
  loadError = false;
  /** „Clear my drawings" bolo použité → DELETE pri save. */
  cleared = false;

  /** Live náhľad obdĺžnika počas ťahania (rect tool). */
  draftRect: { x: number; y: number; w: number; h: number } | null = null;

  private nextLocalKey = -1;
  private history: UserAnnotation[][] = [];
  private historyIndex = 0;

  // Interný stav ťahania
  private drawing = false;
  private anchor: { x: number; y: number } | null = null;
  private dragMode: 'move' | 'resize' | null = null;
  private dragKey: number | null = null;
  private dragStart: { x: number; y: number } | null = null;
  private dragOrig: UserAnnotation | null = null;
  private resizeCorner: 'tl' | 'tr' | 'bl' | 'br' | null = null;

  ngOnInit() {
    const f = this.detection?.flag;
    this.flagChoice = f === 'flagged' || f === 'false_positive' ? f : 'flagged';
    this.loadAnnotations();
  }

  // ── nástroje ──────────────────────────────────────────────────────────

  setTool(tool: FlagTool) {
    this.tool = tool;
    // Rozpracovaný ťah/editor pri prepnutí nástroja sa zruší
    this.draftRect = null;
    this.editingLabelId = null;
  }

  // ── načítanie (anotácie prežijú reload — FR-16 AK) ────────────────────

  private loadAnnotations() {
    if (!this.detection) return;
    this.api.getAnnotations(this.detection.detectionId).subscribe({
      next: list => {
        this.annotations = list.map(a => ({
          key: a.annotationId,
          annotationId: a.annotationId,
          regionX: a.regionX,
          regionY: a.regionY,
          regionW: a.regionW,
          regionH: a.regionH,
          label: a.label,
          dirty: false,
        }));
        // Základ histórie = načítaný stav (ak používateľ ešte nekreslil)
        if (this.history.length <= 1) this.resetHistory();
      },
      error: () => {
        this.loadError = true;
      },
    });
  }

  // ── systémové detekcie (červené boxy, read-only) ──────────────────────

  get systemBoxes(): DetectionRegion[] {
    const d = this.detection;
    if (!d) return [];
    return [{ x: d.regionX, y: d.regionY, width: d.regionW, height: d.regionH, intensity: d.intensity }];
  }

  /** Label systémového boxu: „osoba · 92%" (detekčný typ + konfidencia). */
  systemLabel(): string {
    const d = this.detection;
    if (!d) return '';
    const type = this.i18n.translate(`dash.type.${d.detectionType}`);
    return `${type} · ${Math.round(d.confidence * 100)}%`;
  }

  // ── pointer interakcie na canvase ─────────────────────────────────────

  onPointerDown(event: MouseEvent) {
    if (!this.detection) return;
    const p = this.pointFromEvent(event);
    const target = event.target as Element | null;

    if (this.tool === 'rect') {
      this.drawing = true;
      this.anchor = p;
      this.draftRect = { x: p.x, y: p.y, w: 0, h: 0 };
      return;
    }

    if (this.tool === 'text') {
      const boxEl = target?.closest?.('[data-kind="user-box"]');
      if (boxEl) {
        // Klik na existujúci box → editovať jeho label
        const key = Number(boxEl.getAttribute('data-id'));
        this.selectedKey = key;
        this.editingLabelId = key;
      } else {
        this.addTextAnnotation(p);
      }
      return;
    }

    // select tool
    const handle = target?.closest?.('[data-corner]');
    if (handle) {
      const key = Number(handle.getAttribute('data-id'));
      const box = this.annotations.find(a => a.key === key);
      if (!box) return;
      this.selectedKey = key;
      this.dragMode = 'resize';
      this.dragKey = key;
      this.dragStart = p;
      this.dragOrig = this.cloneAnnotation(box);
      this.resizeCorner = handle.getAttribute('data-corner') as 'tl' | 'tr' | 'bl' | 'br';
      return;
    }
    const boxEl = target?.closest?.('[data-kind="user-box"]');
    if (boxEl) {
      const key = Number(boxEl.getAttribute('data-id'));
      const box = this.annotations.find(a => a.key === key);
      if (!box) return;
      this.selectedKey = key;
      this.dragMode = 'move';
      this.dragKey = key;
      this.dragStart = p;
      this.dragOrig = this.cloneAnnotation(box);
      return;
    }
    // Klik na prázdne miesto → zrušiť výber
    this.selectedKey = null;
  }

  onPointerMove(event: MouseEvent) {
    if (!this.detection) return;
    const p = this.pointFromEvent(event);

    if (this.tool === 'rect' && this.drawing && this.anchor) {
      this.draftRect = this.bbox(this.anchor, p);
      return;
    }
    if (this.dragMode === 'move' && this.dragStart && this.dragOrig && this.dragKey !== null) {
      const box = this.annotations.find(a => a.key === this.dragKey);
      if (!box) return;
      const dx = p.x - this.dragStart.x;
      const dy = p.y - this.dragStart.y;
      box.regionX = Math.min(clamp01(this.dragOrig.regionX + dx), 1 - box.regionW);
      box.regionY = Math.min(clamp01(this.dragOrig.regionY + dy), 1 - box.regionH);
      box.dirty = true;
      return;
    }
    if (this.dragMode === 'resize' && this.dragStart && this.dragOrig && this.dragKey !== null && this.resizeCorner) {
      const box = this.annotations.find(a => a.key === this.dragKey);
      if (!box) return;
      const dx = p.x - this.dragStart.x;
      const dy = p.y - this.dragStart.y;
      this.applyResize(box, this.dragOrig, this.resizeCorner, dx, dy);
      box.dirty = true;
    }
  }

  onPointerUp(_event: MouseEvent) {
    if (!this.detection) return;
    const p = this.pointFromEvent(_event);

    let actionDone = false;
    if (this.tool === 'rect' && this.drawing && this.anchor) {
      const rect = this.bbox(this.anchor, p);
      if (rect.w >= MIN_EDGE && rect.h >= MIN_EDGE) {
        const key = this.nextLocalKey--;
        this.annotations.push({
          key,
          annotationId: 0,
          regionX: rect.x,
          regionY: rect.y,
          regionW: rect.w,
          regionH: rect.h,
          label: '',
          dirty: true,
        });
        this.selectedKey = key;
        actionDone = true;
      }
      this.draftRect = null;
    } else if (this.dragMode !== null && this.dragKey !== null) {
      actionDone = true; // posun/resize dokončený
    }

    this.drawing = false;
    this.anchor = null;
    this.dragMode = null;
    this.dragKey = null;
    this.dragStart = null;
    this.dragOrig = null;
    this.resizeCorner = null;
    if (actionDone) this.pushHistory();
  }

  /** Text tool: nová anotácia na mieste kliku + okamžitá úprava labelu. */
  private addTextAnnotation(p: { x: number; y: number }) {
    const x = clamp01(p.x - TEXT_W / 2);
    const y = clamp01(p.y - TEXT_H / 2);
    const key = this.nextLocalKey--;
    this.annotations.push({
      key,
      annotationId: 0,
      regionX: x,
      regionY: y,
      regionW: TEXT_W,
      regionH: TEXT_H,
      label: '',
      dirty: true,
    });
    this.selectedKey = key;
    this.editingLabelId = key;
    this.pushHistory();
  }

  // ── label editing ─────────────────────────────────────────────────────

  setLabel(key: number, value: string) {
    const box = this.annotations.find(a => a.key === key);
    if (box) {
      box.label = value;
      box.dirty = true;
    }
  }

  startLabelEdit(key: number) {
    this.selectedKey = key;
    this.editingLabelId = key;
  }

  /** Commit labelu (Enter/blur): prázdny label novej anotácie → box sa zruší. */
  commitLabel(key: number) {
    const idx = this.annotations.findIndex(a => a.key === key);
    if (idx < 0) return;
    const box = this.annotations[idx];
    this.editingLabelId = null;
    if (box.label.trim() !== '') return;
    if (box.annotationId === 0) {
      this.annotations.splice(idx, 1);
      if (this.selectedKey === key) this.selectedKey = null;
      this.pushHistory();
    } else {
      box.label = '';
      box.dirty = true;
    }
  }

  onStageKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Delete' && event.key !== 'Backspace') return;
    if (this.editingLabelId !== null) return; // input má focus
    if (this.selectedKey === null) return;
    const idx = this.annotations.findIndex(a => a.key === this.selectedKey);
    if (idx < 0) return;
    const box = this.annotations[idx];
    if (box.annotationId !== 0) return; // uložené sa mažú cez „Clear my drawings"
    this.annotations.splice(idx, 1);
    this.selectedKey = null;
    this.pushHistory();
  }

  // ── undo / redo / clear ───────────────────────────────────────────────

  get canUndo(): boolean {
    return this.historyIndex > 0;
  }

  get canRedo(): boolean {
    return this.historyIndex < this.history.length - 1;
  }

  undo() {
    if (this.historyIndex <= 0) return;
    this.historyIndex--;
    this.annotations = this.cloneAnnotations(this.history[this.historyIndex]);
    this.selectedKey = null;
    this.editingLabelId = null;
  }

  redo() {
    if (this.historyIndex >= this.history.length - 1) return;
    this.historyIndex++;
    this.annotations = this.cloneAnnotations(this.history[this.historyIndex]);
    this.selectedKey = null;
    this.editingLabelId = null;
  }

  /** „Clear my drawings" — maže LEN užívateľské anotácie (lokálne; DELETE pri save). */
  clearDrawings() {
    if (this.annotations.length === 0) return;
    this.annotations = [];
    this.selectedKey = null;
    this.editingLabelId = null;
    this.cleared = true;
    this.pushHistory();
  }

  private pushHistory() {
    this.history = this.history.slice(0, this.historyIndex + 1);
    this.history.push(this.cloneAnnotations(this.annotations));
    this.historyIndex++;
  }

  private resetHistory() {
    this.history = [this.cloneAnnotations(this.annotations)];
    this.historyIndex = 0;
  }

  // ── save / cancel ─────────────────────────────────────────────────────

  setFlagChoice(choice: FlagChoice) {
    this.flagChoice = choice;
    this.flagTouched = true;
  }

  /** Je čo uložiť (flag zmenený / anotácie nové alebo zmenené / clear)? */
  get canSave(): boolean {
    if (this.flagTouched || this.cleared) return true;
    return this.annotations.some(a => a.annotationId === 0 || a.dirty);
  }

  /** Save: DELETE (clear) → POST nové → PUT zmenené → flag (ak zmenený). */
  save() {
    if (!this.detection || this.saving) return;
    const detectionId = this.detection.detectionId;
    this.saving = true;
    this.saveError = false;

    const ops: { run: () => unknown }[] = [];
    if (this.cleared) ops.push({ run: () => this.api.clearMyAnnotations(detectionId) });
    if (this.flagTouched) ops.push({ run: () => this.api.setFlag(detectionId, this.flagChoice) });
    for (const a of this.annotations) {
      const body: AnnotationRequest = {
        regionX: a.regionX,
        regionY: a.regionY,
        regionW: a.regionW,
        regionH: a.regionH,
        label: a.label,
      };
      if (a.annotationId === 0) ops.push({ run: () => this.api.addAnnotation(detectionId, body) });
      else if (a.dirty) ops.push({ run: () => this.api.updateAnnotation(detectionId, a.annotationId, body) });
    }

    const finish = () => {
      this.saving = false;
      this.cleared = false;
      this.flagTouched = false;
      this.annotations = this.annotations.map(a => ({ ...a, dirty: false }));
      this.saved.emit({ flag: this.flagChoice });
    };

    if (ops.length === 0) {
      finish();
      return;
    }
    // Sekvenčne (DELETE pred POST/PUT — žiadny race na rovnakých riadkoch)
    from(ops)
      .pipe(concatMap(op => op.run() as never))
      .subscribe({
        next: () => undefined,
        error: () => {
          this.saving = false;
          this.saveError = true;
        },
        complete: () => finish(),
      });
  }

  onCancel() {
    this.cancel.emit();
  }

  goHome() {
    this.home.emit();
  }

  // ── rendering helpers ─────────────────────────────────────────────────

  /** Box ako percentá (HTML overlay nad canvasom). */
  boxStyle(a: { regionX: number; regionY: number; regionW: number; regionH: number }): Record<string, string> {
    return {
      left: `${(a.regionX * 100).toFixed(3)}%`,
      top: `${(a.regionY * 100).toFixed(3)}%`,
      width: `${(a.regionW * 100).toFixed(3)}%`,
      height: `${(a.regionH * 100).toFixed(3)}%`,
    };
  }

  /** Živý náhľad počas ťahania (rect tool). */
  draftStyle(): Record<string, string> | null {
    const d = this.draftRect;
    if (!d) return null;
    return this.boxStyle({ regionX: d.x, regionY: d.y, regionW: d.w, regionH: d.h });
  }

  private bbox(a: { x: number; y: number }, b: { x: number; y: number }): { x: number; y: number; w: number; h: number } {
    const x = Math.min(a.x, b.x);
    const y = Math.min(a.y, b.y);
    return { x, y, w: Math.abs(b.x - a.x), h: Math.abs(b.y - a.y) };
  }

  /** Resize podľa ťahaného rohu; opačný roh je kotva (nemení sa). */
  private applyResize(
    box: UserAnnotation,
    orig: UserAnnotation,
    corner: 'tl' | 'tr' | 'bl' | 'br',
    dx: number,
    dy: number,
  ) {
    const right = orig.regionX + orig.regionW;
    const bottom = orig.regionY + orig.regionH;
    let newX = orig.regionX;
    let newY = orig.regionY;
    let newW = orig.regionW;
    let newH = orig.regionH;

    switch (corner) {
      case 'br': // kotva: ľavý horný roh
        newW = Math.min(Math.max(MIN_EDGE, orig.regionW + dx), 1 - orig.regionX);
        newH = Math.min(Math.max(MIN_EDGE, orig.regionH + dy), 1 - orig.regionY);
        break;
      case 'tl': { // kotva: pravý dolný roh
        newX = Math.min(clamp01(orig.regionX + dx), right - MIN_EDGE);
        newY = Math.min(clamp01(orig.regionY + dy), bottom - MIN_EDGE);
        newW = right - newX;
        newH = bottom - newY;
        break;
      }
      case 'tr': { // kotva: ľavý dolný roh
        newW = Math.min(Math.max(MIN_EDGE, orig.regionW + dx), 1 - orig.regionX);
        newY = Math.min(clamp01(orig.regionY + dy), bottom - MIN_EDGE);
        newH = bottom - newY;
        break;
      }
      case 'bl': { // kotva: pravý horný roh
        newX = Math.min(clamp01(orig.regionX + dx), right - MIN_EDGE);
        newW = right - newX;
        newH = Math.min(Math.max(MIN_EDGE, orig.regionH + dy), 1 - orig.regionY);
        break;
      }
    }
    box.regionX = newX;
    box.regionY = newY;
    box.regionW = newW;
    box.regionH = newH;
  }

  private pointFromEvent(event: MouseEvent): { x: number; y: number } {
    const el = event.currentTarget as HTMLElement | null;
    const rect = el?.getBoundingClientRect();
    if (!rect || rect.width === 0) return { x: 0, y: 0 };
    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height),
    };
  }

  private cloneAnnotation(a: UserAnnotation): UserAnnotation {
    return { ...a };
  }

  private cloneAnnotations(list: UserAnnotation[]): UserAnnotation[] {
    return list.map(a => this.cloneAnnotation(a));
  }
}
