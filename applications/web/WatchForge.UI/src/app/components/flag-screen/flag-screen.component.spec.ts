import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FlagScreenComponent, UserAnnotation } from './flag-screen.component';
import { ApiService } from '../../services/api.service';
import { I18nService } from '../../i18n/i18n.service';
import { AnnotationDto, AnnotationRequest, DetectionDto } from '../../models/video.model';

/**
 * Flag screen (S6-7, FR-11 + FR-16, design handoff flag-screen.jsx).
 * Červené boxy = systémové detekcie (SYSTEM ·), zelené = užívateľské
 * anotácie (USER ·, resize handles). Nástroje Select / Rectangle / Text label,
 * undo/redo, „Clear my drawings" maže LEN užívateľské anotácie.
 */

function detection(overrides: Partial<DetectionDto> = {}): DetectionDto {
  return {
    detectionId: 1,
    recordingId: 10,
    cameraId: 1,
    detectionType: 'person',
    timestampMs: 5000,
    durationMs: 2000,
    confidence: 0.92,
    algorithmVersion: 'cpu-1',
    configVersionId: 1,
    regionX: 0.33,
    regionY: 0.48,
    regionW: 0.11,
    regionH: 0.36,
    intensity: 70,
    objectClass: '',
    flag: 'none',
    ...overrides,
  };
}

function createFakeApi(overrides: Record<string, unknown> = {}) {
  return {
    getAnnotations: vi.fn(() => of([] as AnnotationDto[])),
    addAnnotation: vi.fn((_id: number, req: AnnotationRequest) =>
      of({
        annotationId: 41,
        detectionId: 1,
        userId: 0,
        regionX: req.regionX,
        regionY: req.regionY,
        regionW: req.regionW,
        regionH: req.regionH,
        label: req.label ?? '',
        createdAt: '2026-08-08T10:00:00Z',
      }),
    ),
    updateAnnotation: vi.fn((_id: number, aid: number, req: AnnotationRequest) =>
      of({
        annotationId: aid,
        detectionId: 1,
        userId: 0,
        regionX: req.regionX,
        regionY: req.regionY,
        regionW: req.regionW,
        regionH: req.regionH,
        label: req.label ?? '',
        createdAt: '2026-08-08T10:00:00Z',
      }),
    ),
    clearMyAnnotations: vi.fn(() => of({ ok: true, detectionId: 1, removed: 2 })),
    setFlag: vi.fn(() => of({ ok: true, detectionId: 1, flag: 'flagged' })),
    ...overrides,
  };
}

/** Pointer event s clientX/Y (canvas 1600×900 meraný na pozícii 0,0). */
function pointerAt(xPx: number, yPx: number, type: string, target?: Element): MouseEvent {
  const ev = new MouseEvent(type, { clientX: xPx, clientY: yPx, bubbles: true });
  if (target) Object.defineProperty(ev, 'target', { value: target, configurable: true });
  return ev;
}

describe('FlagScreenComponent (S6-7)', () => {
  let api: ReturnType<typeof createFakeApi>;

  beforeEach(async () => {
    api = createFakeApi();
    await TestBed.configureTestingModule({
      imports: [FlagScreenComponent],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  function createFixture(det: DetectionDto = detection()) {
    const fixture = TestBed.createComponent(FlagScreenComponent);
    fixture.componentRef.setInput('detection', det);
    fixture.componentRef.setInput('cameraName', 'Dvor');
    fixture.detectChanges();
    // Pointer handlery sú na .flag-canvas (div) — meranie 1600×900 na 0,0
    const canvas = (fixture.nativeElement as HTMLElement).querySelector('.flag-canvas') as HTMLElement;
    if (canvas) {
      vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({
        left: 0, top: 0, right: 1600, bottom: 900,
        width: 1600, height: 900, x: 0, y: 0,
        toJSON: () => ({}),
      } as DOMRect);
    }
    return fixture;
  }

  function svgOf(fixture: ReturnType<typeof createFixture>): SVGSVGElement {
    return (fixture.nativeElement as HTMLElement).querySelector('.flag-svg') as SVGSVGElement;
  }

  function comp(fixture: ReturnType<typeof createFixture>): FlagScreenComponent {
    return fixture.componentInstance;
  }

  /** Nakreslí obdĺžnik ťahom myši (100..300 px → 0.0625..0.1875 normalizované). */
  function drawRect(fixture: ReturnType<typeof createFixture>) {
    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(100, 90, 'pointerdown'));
    svg.dispatchEvent(pointerAt(300, 270, 'pointermove'));
    svg.dispatchEvent(pointerAt(300, 270, 'pointerup'));
  }

  // ── default stav + načítanie anotácií ─────────────────────────────────

  it('default: zobrazí systémový box (SYSTEM ·) a legenda, načíta anotácie z API', () => {
    const fixture = createFixture();
    const el = fixture.nativeElement as HTMLElement;

    expect(api.getAnnotations).toHaveBeenCalledWith(1);
    expect(el.querySelectorAll('.system-box').length).toBe(1);
    expect(el.textContent).toContain('SYSTEM');
    expect(el.textContent).toContain('osoba · 92%');
    // Legenda: systémová + užívateľská
    expect(el.querySelectorAll('.legend-item').length).toBe(2);
  });

  it('načítané anotácie sa zobrazia (prežijú reload — FR-16 AK)', () => {
    // TestBed už má injektovaný pôvodný fake — upravíme návratovú hodnotu
    api.getAnnotations.mockReturnValue(
      of([{
        annotationId: 5, detectionId: 1, userId: 1,
        regionX: 0.56, regionY: 0.62, regionW: 0.09, regionH: 0.22,
        label: 'Taška', createdAt: '2026-08-08T10:00:00Z',
      }]),
    );
    const fixture = createFixture();
    fixture.detectChanges();

    expect(comp(fixture).annotations.length).toBe(1);
    expect(comp(fixture).annotations[0].label).toBe('Taška');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Taška');
  });

  // ── kreslenie (Rectangle) ─────────────────────────────────────────────

  it('rectangle: ťah myšou vytvorí normalizovanú užívateľskú anotáciu (USER)', () => {
    const fixture = createFixture();
    drawRect(fixture);

    const boxes = comp(fixture).annotations;
    expect(boxes.length).toBe(1);
    expect(boxes[0].annotationId).toBe(0); // lokálna, ešte neuložená
    expect(boxes[0].key).toBeLessThan(0);
    expect(boxes[0].regionX).toBeCloseTo(100 / 1600, 5);
    expect(boxes[0].regionY).toBeCloseTo(90 / 900, 5);
    expect(boxes[0].regionW).toBeCloseTo(200 / 1600, 5);
    expect(boxes[0].regionH).toBeCloseTo(180 / 900, 5);
    // Save sa aktivuje
    fixture.detectChanges();
    expect(comp(fixture).canSave).toBe(true);
  });

  it('rectangle: príliš malý ťah (klik) nevytvorí anotáciu', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(100, 90, 'pointerdown'));
    svg.dispatchEvent(pointerAt(101, 91, 'pointermove'));
    svg.dispatchEvent(pointerAt(101, 91, 'pointerup'));
    expect(comp(fixture).annotations.length).toBe(0);
  });

  // ── Select: posun a resize ────────────────────────────────────────────

  it('select: ťahanie boxu ho posunie (normalizované súradnice)', () => {
    const fixture = createFixture();
    drawRect(fixture);
    const before = { ...comp(fixture).annotations[0] };

    comp(fixture).setTool('select');
    fixture.detectChanges();
    const boxEl = (fixture.nativeElement as HTMLElement).querySelector('.user-box') as Element;

    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(200, 180, 'pointerdown', boxEl));
    svg.dispatchEvent(pointerAt(360, 270, 'pointermove'));
    svg.dispatchEvent(pointerAt(360, 270, 'pointerup'));

    const moved = comp(fixture).annotations[0];
    expect(moved.regionX).toBeCloseTo(before.regionX + 160 / 1600, 5);
    expect(moved.regionY).toBeCloseTo(before.regionY + 90 / 900, 5);
    expect(moved.regionW).toBeCloseTo(before.regionW, 5);
    expect(moved.regionH).toBeCloseTo(before.regionH, 5);
    expect(moved.dirty).toBe(true);
  });

  it('select: ťahanie za roh (br handle) zmení veľkosť, opačný roh ostáva', () => {
    const fixture = createFixture();
    drawRect(fixture);
    comp(fixture).setTool('select');
    fixture.detectChanges();
    const handle = (fixture.nativeElement as HTMLElement).querySelector('[data-corner="br"]') as Element;

    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(300, 270, 'pointerdown', handle));
    svg.dispatchEvent(pointerAt(420, 450, 'pointermove'));
    svg.dispatchEvent(pointerAt(420, 450, 'pointerup'));

    const box = comp(fixture).annotations[0];
    expect(box.regionW).toBeCloseTo(320 / 1600, 5);
    expect(box.regionH).toBeCloseTo(360 / 900, 5);
    // Ľavý horný roh (kotva) sa nehýbe
    expect(box.regionX).toBeCloseTo(100 / 1600, 5);
    expect(box.regionY).toBeCloseTo(90 / 900, 5);
  });

  // ── Text label ────────────────────────────────────────────────────────

  it('text: klik vytvorí box s label editorom; commitLabel uloží text', () => {
    const fixture = createFixture();
    comp(fixture).setTool('text');
    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(800, 450, 'pointerdown'));

    const key = comp(fixture).annotations[0]?.key;
    expect(key).toBeLessThan(0);
    expect(comp(fixture).editingLabelId).toBe(key);

    comp(fixture).setLabel(key, 'Taška');
    comp(fixture).commitLabel(key);
    expect(comp(fixture).annotations[0].label).toBe('Taška');
    expect(comp(fixture).editingLabelId).toBeNull();
  });

  it('text: commit s prázdnym labelom box odstráni', () => {
    const fixture = createFixture();
    comp(fixture).setTool('text');
    const svg = svgOf(fixture);
    svg.dispatchEvent(pointerAt(800, 450, 'pointerdown'));
    const key = comp(fixture).annotations[0].key;

    comp(fixture).commitLabel(key);
    expect(comp(fixture).annotations.length).toBe(0);
  });

  // ── undo / redo / clear ───────────────────────────────────────────────

  it('undo vráti kreslenie, redo ho obnoví', () => {
    const fixture = createFixture();
    drawRect(fixture);
    expect(comp(fixture).canUndo).toBe(true);
    comp(fixture).undo();
    expect(comp(fixture).annotations.length).toBe(0);
    comp(fixture).redo();
    expect(comp(fixture).annotations.length).toBe(1);
  });

  it('clearDrawings maže LEN užívateľské anotácie — systémový box ostáva, DELETE ide až pri save', () => {
    const fixture = createFixture();
    drawRect(fixture);
    comp(fixture).clearDrawings();

    expect(comp(fixture).annotations.length).toBe(0);
    expect(api.clearMyAnnotations).not.toHaveBeenCalled(); // lokálne, commit pri save
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.system-box').length).toBe(1); // systém ostáva

    comp(fixture).save();
    expect(api.clearMyAnnotations).toHaveBeenCalledWith(1);
  });

  // ── save / cancel ─────────────────────────────────────────────────────

  it('save: POST nové, PUT zmenené, flag len ak zmenený, emituje saved', () => {
    const fixture = createFixture();
    drawRect(fixture); // nová lokálna
    comp(fixture).setTool('select');
    fixture.detectChanges();
    // Posun existujúcej načítanej anotácie
    const loaded: UserAnnotation = {
      key: 5, annotationId: 5, regionX: 0.5, regionY: 0.5,
      regionW: 0.1, regionH: 0.1, label: 'Staré', dirty: false,
    };
    comp(fixture).annotations.push(loaded);
    comp(fixture).annotations[0].dirty = true; // nová
    loaded.regionX = 0.6;
    loaded.dirty = true;

    let emitted: { flag: string } | null = null;
    comp(fixture).saved.subscribe(e => (emitted = e));
    comp(fixture).setFlagChoice('false_positive');
    comp(fixture).save();
    fixture.detectChanges();

    expect(api.addAnnotation).toHaveBeenCalledTimes(1);
    expect(api.updateAnnotation).toHaveBeenCalledWith(1, 5, expect.objectContaining({ regionX: 0.6 }));
    expect(api.setFlag).toHaveBeenCalledWith(1, 'false_positive');
    expect(emitted).toEqual({ flag: 'false_positive' });
  });

  it('save bez zmeny flagu nevolá setFlag a emituje saved', () => {
    const fixture = createFixture();
    let emitted = false;
    comp(fixture).saved.subscribe(() => (emitted = true));
    comp(fixture).save();
    expect(api.setFlag).not.toHaveBeenCalled();
    expect(emitted).toBe(true);
  });

  it('cancel emituje cancel bez API mutácií', () => {
    const fixture = createFixture();
    let cancelled = false;
    comp(fixture).cancel.subscribe(() => (cancelled = true));
    comp(fixture).onCancel();
    expect(cancelled).toBe(true);
    expect(api.setFlag).not.toHaveBeenCalled();
    expect(api.addAnnotation).not.toHaveBeenCalled();
  });

  // ── flag choice ───────────────────────────────────────────────────────

  it('flagChoice default: z detection.flag (false_positive sa predvyplní)', () => {
    const fixture = createFixture(detection({ flag: 'false_positive' }));
    expect(comp(fixture).flagChoice).toBe('false_positive');
  });

  it('flagChoice default pri flag=none je flagged', () => {
    const fixture = createFixture(detection({ flag: 'none' }));
    expect(comp(fixture).flagChoice).toBe('flagged');
  });
});
