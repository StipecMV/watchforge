import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { TimelineComponent } from './timeline.component';
import { I18nService } from '../../i18n/i18n.service';
import { DetectionEvent } from '../../models/video.model';

function fakeCtx() {
  const ctx: Record<string, unknown> = {};
  for (const m of [
    'fillRect', 'clearRect', 'strokeRect', 'beginPath', 'moveTo', 'lineTo',
    'stroke', 'fill', 'closePath', 'fillText',
  ]) {
    ctx[m] = vi.fn();
  }
  ctx['canvas'] = { width: 0, height: 0 };
  return ctx as unknown as CanvasRenderingContext2D;
}

const EVENTS: DetectionEvent[] = [
  { timestampMs: 5_000, durationMs: 2_000, regions: [] },
  { timestampMs: 15_000, durationMs: 1_500, regions: [] },
  { timestampMs: 30_000, durationMs: 3_000, regions: [] },
];

describe('TimelineComponent (S6-3, zoom timeline)', () => {
  let getCtx: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    // jsdom nemá ResizeObserver — komponent ho používa na redraw pri resize.
    (globalThis as Record<string, unknown>)['ResizeObserver'] = class {
      observe() {}
      disconnect() {}
      unobserve() {}
    };
    getCtx = vi
      .spyOn(HTMLCanvasElement.prototype, 'getContext')
      .mockReturnValue(fakeCtx());
    await TestBed.configureTestingModule({
      imports: [TimelineComponent],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  afterEach(() => getCtx.mockRestore());

  function createFixture(duration = 60) {
    const fixture = TestBed.createComponent(TimelineComponent);
    fixture.componentRef.setInput('duration', duration);
    fixture.componentRef.setInput('events', EVENTS);
    fixture.componentRef.setInput('currentTime', 10);
    fixture.detectChanges();
    return fixture;
  }

  it('po načítaní duration je zobrazený celý rozsah (nie zoom)', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    expect(comp.isZoomed).toBe(false);
    expect(comp.thumbLeft).toBe(0);
    expect(comp.thumbWidth).toBe(100);
  });

  it('zoomIn zúži okno okolo aktuálneho času a zapne mini scrollbar', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    comp.zoomIn();
    expect(comp.isZoomed).toBe(true);
    expect(comp.thumbWidth).toBeLessThan(100);
    expect(comp.thumbLeft).toBeGreaterThan(0);
  });

  it('zoomOut po viacnásobnom zoomIn sa vráti na celý rozsah', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    comp.zoomIn();
    comp.zoomIn();
    comp.zoomOut();
    comp.zoomOut();
    expect(comp.isZoomed).toBe(false);
    expect(comp.thumbLeft).toBe(0);
    expect(comp.thumbWidth).toBe(100);
  });

  it('resetZoom vráti zobrazenie na celý rozsah', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    comp.zoomIn();
    expect(comp.isZoomed).toBe(true);
    comp.resetZoom();
    expect(comp.isZoomed).toBe(false);
    expect(comp.thumbLeft).toBe(0);
    expect(comp.thumbWidth).toBe(100);
  });

  it('klik na canvas emituje seek na zodpovedajúci čas', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    let seeked: number | null = null;
    comp.seek.subscribe(t => (seeked = t));

    const canvas = fixture.nativeElement.querySelector('canvas') as HTMLCanvasElement;
    Object.defineProperty(canvas, 'offsetWidth', { value: 600, configurable: true });
    Object.defineProperty(canvas, 'offsetHeight', { value: 52, configurable: true });
    comp.draw(); // prepíše canvas.width na 600 (draw používa offsetWidth)
    // Klik v strede → čas 30s
    comp.onClick({ clientX: 300, button: 0 } as MouseEvent);

    expect(seeked).toBeCloseTo(30, 1);
  });

  it('wheel zoom priblíži okolo kurzora', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance;
    comp.zoomIn(); // zúžime na ~40s okno
    const before = comp.thumbWidth;
    const canvas = fixture.nativeElement.querySelector('canvas') as HTMLCanvasElement;
    Object.defineProperty(canvas, 'offsetWidth', { value: 600, configurable: true });
    Object.defineProperty(canvas, 'getBoundingClientRect', {
      value: () => ({ left: 0, width: 600 }),
      configurable: true,
    });

    canvas.dispatchEvent(new WheelEvent('wheel', { deltaY: -100, clientX: 300 }));
    expect(comp.thumbWidth).toBeLessThan(before);
  });

  it('pri prázdnom duration nekreslí a nezahodí seek', () => {
    const fixture = createFixture(0);
    const comp = fixture.componentInstance;
    let seeked = false;
    comp.seek.subscribe(() => (seeked = true));
    comp.onClick({ clientX: 100, button: 0 } as MouseEvent);
    expect(seeked).toBe(false);
  });

  it('formatTime vráti m:ss pre krátke a h:mm:ss pre dlhé časy', () => {
    const fixture = createFixture(60);
    const comp = fixture.componentInstance as unknown as { formatTime(s: number): string };
    expect(comp.formatTime(65)).toBe('1:05');
    expect(comp.formatTime(3661)).toBe('1:01:01');
  });
});
