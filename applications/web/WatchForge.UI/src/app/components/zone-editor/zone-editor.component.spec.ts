import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ZoneEditorComponent } from './zone-editor.component';
import { I18nService } from '../../i18n/i18n.service';
import { CameraDto } from '../../models/video.model';
import { DetectionProfile, ZoneDto } from '../../models/settings.model';

const CAMERAS: CameraDto[] = [
  { cameraId: 1, nvrId: 1, channel: 0, friendlyName: 'Dvor', iconId: 'garden', isActive: true },
  { cameraId: 2, nvrId: 1, channel: 1, friendlyName: 'Brána', iconId: 'gate', isActive: true },
];

const PROFILE: DetectionProfile = {
  configVersionId: 4,
  cameraId: 1,
  profileType: 'per_camera',
  sensitivity: 0.6,
  intensityThreshold: 0.03,
  minContourArea: 0.001,
  ignoreZones: [{ x: 0.7, y: 0.5, w: 0.1, h: 0.2, name: 'Driveway entry', shape: 'rect' }],
  focusZones: [],
};

/** Súradnice pointera → normalizované 0..1 (canvas 1600×900). */
function eventAt(xPx: number, yPx: number, type: string): MouseEvent {
  return new MouseEvent(type, { clientX: xPx, clientY: yPx, bubbles: true });
}

describe('ZoneEditorComponent (S6-6)', () => {
  let i18n: I18nService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ZoneEditorComponent],
    }).compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLocale('sk');
  });

  function createFixture(overrides: { initialZone?: ZoneDto | null; initialType?: 'ignore' | 'focus' } = {}) {
    const fixture = TestBed.createComponent(ZoneEditorComponent);
    const comp = fixture.componentInstance;
    fixture.componentRef.setInput('cameras', CAMERAS.map(c => ({ ...c })));
    fixture.componentRef.setInput('profile', PROFILE);
    fixture.componentRef.setInput('initialZone', overrides.initialZone ?? null);
    fixture.componentRef.setInput('initialType', overrides.initialType ?? 'ignore');
    fixture.detectChanges();
    // Canvas merania: 1600×900 na pozícii 0,0
    const svg = (fixture.nativeElement as HTMLElement).querySelector('.zone-canvas svg') as SVGSVGElement;
    vi.spyOn(svg, 'getBoundingClientRect').mockReturnValue({
      left: 0, top: 0, right: 1600, bottom: 900,
      width: 1600, height: 900, x: 0, y: 0,
      toJSON: () => ({}),
    } as DOMRect);
    return fixture;
  }

  function svgOf(fixture: ReturnType<typeof createFixture>): SVGSVGElement {
    return (fixture.nativeElement as HTMLElement).querySelector('.zone-canvas svg') as SVGSVGElement;
  }

  // ── default stav ───────────────────────────────────────────────────────

  it('default: nástroj rectangle, typ ignore, save je disabled bez nakreslenej zóny', () => {
    const fixture = createFixture();
    const el = fixture.nativeElement as HTMLElement;
    const saveBtn = el.querySelector('.save-zone') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(true);
    expect(comp(fixture).tool).toBe('rect');
    expect(comp(fixture).zoneType).toBe('ignore');
  });

  it('zobrazí existujúce zóny profilu ako read-only', () => {
    const fixture = createFixture();
    const zones = (fixture.nativeElement as HTMLElement).querySelectorAll('.existing-zone');
    expect(zones.length).toBe(1);
    expect((zones[0] as HTMLElement).textContent).toContain('Driveway entry');
  });

  // ── kreslenie obdĺžnika ────────────────────────────────────────────────

  it('rectangle: potiahnutie myšou vytvorí normalizovaný bbox draft', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointermove'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));

    const draft = comp(fixture).draft;
    expect(draft).not.toBeNull();
    expect(draft!.x).toBeCloseTo(0.1, 5);
    expect(draft!.y).toBeCloseTo(0.1, 5);
    expect(draft!.w).toBeCloseTo(0.2, 5);
    expect(draft!.h).toBeCloseTo(0.2, 5);
    expect(draft!.shape).toBe('rect');
    // Save sa aktivuje
    fixture.detectChanges();
    const saveBtn = (fixture.nativeElement as HTMLElement).querySelector('.save-zone') as HTMLButtonElement;
    expect(saveBtn.disabled).toBe(false);
  });

  it('rectangle: ťahanie mimo canvas sa oreže na 0..1', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(1600, 900, 'pointerdown'));
    svg.dispatchEvent(eventAt(2400, 1500, 'pointermove'));
    svg.dispatchEvent(eventAt(2400, 1500, 'pointerup'));

    const draft = comp(fixture).draft!;
    expect(draft.x + draft.w).toBeLessThanOrEqual(1.0001);
    expect(draft.y + draft.h).toBeLessThanOrEqual(1.0001);
  });

  // ── polygon ────────────────────────────────────────────────────────────

  it('polygon: kliky pridávajú body, dvojklik dokončí → bbox + points', () => {
    const fixture = createFixture();
    comp(fixture).setTool('polygon');
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'click'));
    svg.dispatchEvent(eventAt(480, 90, 'click'));
    svg.dispatchEvent(eventAt(480, 270, 'click'));
    svg.dispatchEvent(eventAt(160, 270, 'dblclick'));

    const draft = comp(fixture).draft!;
    expect(draft.shape).toBe('polygon');
    expect(draft.points!.length).toBe(4);
    expect(draft.points![0]).toEqual([0.1, 0.1]);
    expect(draft.w).toBeCloseTo(0.2, 5);
    expect(draft.h).toBeCloseTo(0.2, 5);
  });

  it('polygon: Enter dokončí kreslenie', () => {
    const fixture = createFixture();
    comp(fixture).setTool('polygon');
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'click'));
    svg.dispatchEvent(eventAt(480, 270, 'click'));
    svg.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(comp(fixture).draft).not.toBeNull();
    expect(comp(fixture).draft!.shape).toBe('polygon');
  });

  // ── freehand ───────────────────────────────────────────────────────────

  it('freehand: potiahnutie zbiera body, pointerup dokončí → bbox + body', () => {
    const fixture = createFixture();
    comp(fixture).setTool('freehand');
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(200, 120, 'pointermove'));
    svg.dispatchEvent(eventAt(240, 150, 'pointermove'));
    svg.dispatchEvent(eventAt(240, 150, 'pointerup'));

    const draft = comp(fixture).draft!;
    expect(draft.shape).toBe('freehand');
    expect(draft.points!.length).toBeGreaterThanOrEqual(2);
    expect(draft.x).toBeCloseTo(0.1, 5);
    expect(draft.y).toBeCloseTo(0.1, 5);
  });

  // ── typ zóny ───────────────────────────────────────────────────────────

  it('type toggle: prepnutie na focus zmení zoneType a hint', () => {
    const fixture = createFixture();
    comp(fixture).setZoneType('focus');
    expect(comp(fixture).zoneType).toBe('focus');
    fixture.detectChanges();
    const hint = (fixture.nativeElement as HTMLElement).querySelector('.type-hint')?.textContent ?? '';
    expect(hint.length).toBeGreaterThan(0);
  });

  // ── undo / redo / clear ────────────────────────────────────────────────

  it('undo vráti posledný krok kreslenia, redo ho obnoví', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));
    expect(comp(fixture).draft).not.toBeNull();

    comp(fixture).undo();
    expect(comp(fixture).draft).toBeNull();

    comp(fixture).redo();
    expect(comp(fixture).draft).not.toBeNull();
  });

  it('clear zmaže draft (undo ho vráti)', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));

    comp(fixture).clear();
    expect(comp(fixture).draft).toBeNull();

    comp(fixture).undo();
    expect(comp(fixture).draft).not.toBeNull();
  });

  // ── štatistiky tvaru ───────────────────────────────────────────────────

  it('stats: X/Y/W×H/Area/points sa počítajú z draftu (px v 1600×900)', () => {
    const fixture = createFixture();
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));
    fixture.detectChanges();

    const stats = comp(fixture).stats;
    expect(stats.xPx).toBe(160);
    expect(stats.yPx).toBe(90);
    expect(stats.wPx).toBe(320);
    expect(stats.hPx).toBe(180);
    expect(stats.areaPct).toBeCloseTo(4, 0);
    expect(stats.points).toBe(4);
  });

  // ── save ───────────────────────────────────────────────────────────────

  it('save emituje zónu s menom, typom, tvarom a bodmi', () => {
    const fixture = createFixture();
    const compInstance = comp(fixture);
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));
    compInstance.name = 'Mailbox area';
    compInstance.setZoneType('focus');
    fixture.detectChanges();

    const emitted = vi.fn();
    compInstance.saveZone.subscribe(emitted);
    (fixture.nativeElement as HTMLElement).querySelector('.save-zone')?.dispatchEvent(new Event('click'));

    expect(emitted).toHaveBeenCalledOnce();
    const event = emitted.mock.calls[0][0] as { name: string; type: string; cameraId: number; zone: ZoneDto };
    expect(event.name).toBe('Mailbox area');
    expect(event.type).toBe('focus');
    expect(event.cameraId).toBe(1); // prvá aktívna kamera
    expect(event.zone.x).toBeCloseTo(0.1, 5);
    expect(event.zone.w).toBeCloseTo(0.2, 5);
    expect(event.zone.shape).toBe('rect');
  });

  it('save s prázdnym menom použije default meno', () => {
    const fixture = createFixture();
    const compInstance = comp(fixture);
    const svg = svgOf(fixture);
    svg.dispatchEvent(eventAt(160, 90, 'pointerdown'));
    svg.dispatchEvent(eventAt(480, 270, 'pointerup'));

    const emitted = vi.fn();
    compInstance.saveZone.subscribe(emitted);
    compInstance.save();
    expect((emitted.mock.calls[0][0] as { name: string }).name.length).toBeGreaterThan(0);
  });

  it('cancel emituje cancel', () => {
    const fixture = createFixture();
    const emitted = vi.fn();
    comp(fixture).cancel.subscribe(emitted);
    (fixture.nativeElement as HTMLElement).querySelector('.cancel-zone')?.dispatchEvent(new Event('click'));
    expect(emitted).toHaveBeenCalledOnce();
  });

  // ── edit mode ──────────────────────────────────────────────────────────

  it('edit mode: initialZone predvyplní draft, meno a typ', () => {
    const fixture = createFixture({
      initialZone: { x: 0.1, y: 0.2, w: 0.3, h: 0.4, name: 'Old zone', shape: 'polygon', points: [[0.1, 0.2], [0.4, 0.2], [0.4, 0.6]] },
      initialType: 'focus',
    });
    const compInstance = comp(fixture);
    expect(compInstance.draft).not.toBeNull();
    expect(compInstance.draft!.shape).toBe('polygon');
    expect(compInstance.name).toBe('Old zone');
    expect(compInstance.zoneType).toBe('focus');

    const emitted = vi.fn();
    compInstance.saveZone.subscribe(emitted);
    compInstance.save();
    const event = emitted.mock.calls[0][0] as { zone: ZoneDto; type: string };
    expect(event.zone.points!.length).toBe(3);
    expect(event.type).toBe('focus');
  });

  // ── výber kamery ───────────────────────────────────────────────────────

  it('výber kamery nastaví cameraId a emituje cameraChange', () => {
    const fixture = createFixture();
    const compInstance = comp(fixture);
    const emitted = vi.fn();
    compInstance.cameraChange.subscribe(emitted);
    compInstance.selectCamera(2);
    expect(compInstance.cameraId).toBe(2);
    expect(emitted).toHaveBeenCalledWith(2);
  });

  // helper: komponent z fixture
  function comp(fixture: ReturnType<typeof createFixture>): ZoneEditorComponent {
    return fixture.componentInstance;
  }
});
