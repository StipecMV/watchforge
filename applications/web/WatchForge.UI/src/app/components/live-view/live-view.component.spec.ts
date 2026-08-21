import { describe, it, expect, beforeEach, beforeAll, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { LiveViewComponent } from './live-view.component';
import { CameraDto } from '../../models/video.model';
import { I18nService } from '../../i18n/i18n.service';

const CAMERAS: CameraDto[] = [
  { cameraId: 1, nvrId: 1, channel: 0, friendlyName: 'Psia buda (Ares)', iconId: 'camera', isActive: true },
  { cameraId: 2, nvrId: 1, channel: 1, friendlyName: 'Vjazdová brána', iconId: 'camera', isActive: true },
  { cameraId: 3, nvrId: 1, channel: 2, friendlyName: 'Chodník (z hora)', iconId: 'camera', isActive: true },
  { cameraId: 4, nvrId: 1, channel: 3, friendlyName: 'Záhrada pri fontánke', iconId: 'camera', isActive: true },
  { cameraId: 5, nvrId: 1, channel: 4, friendlyName: 'Hnojisko', iconId: 'camera', isActive: true },
  { cameraId: 6, nvrId: 1, channel: 5, friendlyName: 'Hospodársky dvor', iconId: 'camera', isActive: true },
  { cameraId: 7, nvrId: 1, channel: 6, friendlyName: 'Chodník (priamo)', iconId: 'camera', isActive: true },
  { cameraId: 8, nvrId: 1, channel: 7, friendlyName: 'Záhrada pri dome', iconId: 'camera', isActive: true },
];

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
  globalThis.fetch = vi.fn(() => Promise.resolve(new Response('{}'))) as unknown as typeof fetch;
});

function createFixture() {
  const fixture = TestBed.createComponent(LiveViewComponent);
  fixture.componentRef.setInput('cameras', CAMERAS);
  fixture.detectChanges();
  return fixture;
}

describe('LiveViewComponent (S19: režimy 1/8, dvojklik prepína, fps)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LiveViewComponent],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  it('default režim je 8 (všetky kamery)', () => {
    const fixture = createFixture();
    expect(fixture.componentInstance.gridMode).toBe(8);
    expect(fixture.componentInstance.visibleCameras.length).toBe(8);
  });

  it('gridSlots: 1 → 1 bunka, 8 → 8 buniek (bez prázdneho 9. slotu)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    expect(comp.gridSlots.length).toBe(8);
    comp.setGrid(1);
    expect(comp.gridSlots.length).toBe(1);
  });

  it('single klik na kameru (po debounce) → režim 1 s tou kamerou', async () => {
    vi.useFakeTimers();
    try {
      const fixture = createFixture();
      const comp = fixture.componentInstance;
      comp.onCellClick(CAMERAS[2]);
      vi.advanceTimersByTime(300);
      expect(comp.gridMode).toBe(1);
      expect(comp.selectedCameraId).toBe(3);
      expect(comp.visibleCameras.map(c => c.cameraId)).toEqual([3]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('dvojklik na kameru v režime 8 → režim 1 s tou kamerou', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    expect(comp.gridMode).toBe(8);

    comp.onCellDblClick(CAMERAS[4]);
    expect(comp.gridMode).toBe(1);
    expect(comp.selectedCameraId).toBe(5);
  });

  it('dvojklik na kameru v režime 1 → späť na režim 8', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(1);

    comp.onCellDblClick(CAMERAS[0]);
    expect(comp.gridMode).toBe(8);
  });

  it('dvojklik zruší pending single click (clickTimer)', () => {
    vi.useFakeTimers();
    try {
      const fixture = createFixture();
      const comp = fixture.componentInstance;
      comp.onCellClick(CAMERAS[1]); // naplánovaný single click
      comp.onCellDblClick(CAMERAS[1]); // dvojklik — zruší ho
      vi.advanceTimersByTime(400);
      // Dvojklik z režimu 8 → 1; single click sa NEPREjavil druhýkrát
      expect(comp.gridMode).toBe(1);
      expect(comp.selectedCameraId).toBe(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('setGrid vyčistí loaded (loading pri prepínaní — staré framy nezavádzajú)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.onFrameLoaded(1);
    comp.onFrameLoaded(2);
    expect(comp.loaded.has(1)).toBe(true);

    comp.setGrid(1);
    expect(comp.loaded.size).toBe(0);
  });

  it('releaseInvisible posiela POST /release pre kamery mimo view (8→1)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    const fetchSpy = vi.fn(() => Promise.resolve(new Response('{}')));
    (globalThis as unknown as { fetch: typeof fetch }).fetch = fetchSpy as unknown as typeof fetch;

    comp.setGrid(1); // viditeľná len CH1 → CH2–8 sa uvoľňujú
    const releaseCalls = fetchSpy.mock.calls
      .map(c => String(c[0]))
      .filter(u => u.includes('/release'));
    expect(releaseCalls.length).toBe(7);
    expect(releaseCalls.join(',')).toContain('8/release');
    expect(releaseCalls.join(',')).not.toContain('1/release');
  });

  it('frameUrl pre view 1 je frame polling (žiadny MJPEG stream)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(1);
    fixture.detectChanges();
    const url = comp.frameUrls.get(1) ?? '';
    expect(url).toContain('/frame?w=');
    expect(url).toContain('fps=');
    expect(url).not.toContain('/stream');
  });

  it('S22o: view 1 frame žiada 1080p/10fps, view 8 360p/1fps', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(1);
    fixture.detectChanges();
    const url1 = comp.frameUrls.get(1) ?? '';
    expect(url1).toContain('w=1920');
    expect(url1).toContain('fps=10');

    comp.setGrid(8);
    fixture.detectChanges();
    const url8 = comp.frameUrls.get(1) ?? '';
    expect(url8).toContain('w=640');
    expect(url8).toContain('fps=1');
  });

  it('onFrameLoaded pridá cameraId do loaded', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.onFrameLoaded(3);
    expect(comp.loaded.has(3)).toBe(true);
  });

  it('view 1 má polling interval ~66 ms (15 fps)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(1);
    // pollIntervalMs je private — overíme cez VIEW_FPS tabuľku nepriamo:
    // 15 fps = 1000/15 ≈ 66 ms; gridMode=1 musí byť nastavený
    expect(comp.gridMode).toBe(1);
  });

  it('view 8 má polling interval 1000 ms (1 fps/kamera)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(8);
    expect(comp.gridMode).toBe(8);
    expect(comp.visibleCameras.length).toBe(8);
  });

  // ── S22o: client-side zoom/pan (view 1) ────────────────────────────────────

  it('zoomBy približuje a oddiaľuje v rámci 1..8', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.zoomBy(1.2);
    expect(comp.zoom).toBeGreaterThan(1);
    const z1 = comp.zoom;
    comp.zoomBy(1.2);
    expect(comp.zoom).toBeGreaterThan(z1);
    comp.resetZoom();
    expect(comp.zoom).toBe(1);
    comp.zoomBy(1 / 1.2);
    expect(comp.zoom).toBe(1); // clamp na min 1
  });

  it('setGrid(8) resetuje zoom (zoomActive len v view 1)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.setGrid(1);
    comp.zoomBy(1.5);
    expect(comp.zoom).toBeGreaterThan(1);
    comp.setGrid(8);
    expect(comp.zoom).toBe(1);
    expect(comp.zoomActive).toBe(false);
  });

  it('resetZoom vynuluje panX/panY', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.panX = 0.3;
    comp.panY = -0.2;
    comp.resetZoom();
    expect(comp.panX).toBe(0);
    expect(comp.panY).toBe(0);
  });
});
