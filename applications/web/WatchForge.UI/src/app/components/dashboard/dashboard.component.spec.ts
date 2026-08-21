import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../services/api.service';
import { I18nService } from '../../i18n/i18n.service';
import {
  CameraDto,
  DetectionDto,
  RecordingDto,
  RequestStatusDto,
} from '../../models/video.model';

const CAMERA: CameraDto = {
  cameraId: 1,
  nvrId: 1,
  channel: 0,
  friendlyName: 'Dvor',
  iconId: 'garden',
  isActive: true,
};

const RECORDING: RecordingDto = {
  recordingId: 10,
  nvrId: 1,
  cameraId: 1,
  sourceType: 'segment',
  nvrFilename: 'CH01_20260808_140000.mp4',
  beginTime: '2026-08-08T14:00:00Z',
  endTime: '2026-08-08T14:15:00Z',
  durationSec: 900,
  sizeBytes: 1234,
  codec: 'hevc',
  width: 3840,
  height: 2160,
  availability: 'available',
  analysisState: 'completed',
  persisted: false,
};

function detection(
  id: number,
  tsMs: number,
  type = 'motion',
  overrides: Partial<DetectionDto> = {},
): DetectionDto {
  return {
    detectionId: id,
    recordingId: 10,
    cameraId: 1,
    detectionType: type,
    timestampMs: tsMs,
    durationMs: 2000,
    confidence: 0.87,
    algorithmVersion: 'cpu-1',
    configVersionId: 1,
    regionX: 0.2,
    regionY: 0.3,
    regionW: 0.1,
    regionH: 0.2,
    intensity: 42,
    objectClass: '',
    flag: 'none',
    ...overrides,
  };
}

/**
 * RECORDING s beginTime/endTime nastavenými na DNES (12:00 lokálneho času).
 * TODAY filter v komponente počíta udalosti len v aktuálnom dni — hardcoded
 * dátum by testy zlomil po prechode polnoci (dátumová citlivosť, oprava 2026-08-09).
 */
function todayRecording(): RecordingDto {
  // Začiatok 5 min pred teraz → vždy v poslednej hodine (rolling 60 min, S22m)
  const now = new Date();
  const begin = new Date(now.getTime() - 5 * 60 * 1000).toISOString();
  const end = new Date(now.getTime() + 10 * 60 * 1000).toISOString();
  return { ...RECORDING, beginTime: begin, endTime: end };
}

const CAMERA2: CameraDto = {
  cameraId: 2,
  nvrId: 1,
  channel: 1,
  friendlyName: 'Brána',
  iconId: 'gate',
  isActive: true,
};

function status(overrides: Partial<RequestStatusDto>): RequestStatusDto {
  return { requestId: 7, status: 'queued', estimate: '~2 min', clipIds: [], error: null, ...overrides };
}

function createFakeApi(overrides: Record<string, unknown> = {}) {
  return {
    getCameras: vi.fn(() => of([CAMERA])),
    getDetections: vi.fn(() =>
      of({
        total: 3,
        items: [detection(1, 5_000), detection(2, 60_000, 'person'), detection(3, 120_000)],
      }),
    ),
    getRecordings: vi.fn(() => of([RECORDING])),
    createRequest: vi.fn(() => of(status({ status: 'queued' }))),
    getRequestStatus: vi.fn(() => of(status({ status: 'processing' }))),
    clipUrl: vi.fn((id: number) => `http://localhost:5000/api/v1/clips/${id}`),
    recordingVideoUrl: vi.fn((id: number) => `http://localhost:5000/api/v1/recordings/${id}/video`),
    ...overrides,
  };
}

describe('DashboardComponent (S6-3, event-first)', () => {
  let api: ReturnType<typeof createFakeApi>;

  beforeEach(async () => {
    api = createFakeApi();
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function createFixture() {
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.componentRef.setInput('camera', CAMERA);
    fixture.detectChanges();
    return fixture;
  }

  it('bez kamery zobrazí výzvu a nevolá API', () => {
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Vyberte kameru');
    expect(api.getDetections).not.toHaveBeenCalled();
    expect(api.getRecordings).not.toHaveBeenCalled();
  });

  it('s kamerou načíta detekcie a záznamy dňa a automaticky vyberie prvú udalosť', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    expect(api.getDetections).toHaveBeenCalledWith(
      expect.objectContaining({ cameraId: 1 }),
    );
    expect(api.getRecordings).toHaveBeenCalledWith(
      expect.objectContaining({ cameraId: 1 }),
    );
    expect(comp.events.length).toBe(3);
    expect(comp.selectedIndex).toBe(0);
    // Absolútny čas = beginTime záznamu + timestampMs detekcie
    expect(comp.events[0].absoluteTime.toISOString()).toBe('2026-08-08T14:00:05.000Z');
  });

  it('výber udalosti prehrá LOKÁLNE video nahrávky (S22l — žiadny request)', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.selectEvent(1);
    fixture.detectChanges();

    // Žiadny trigger — createRequest sa NESMIE volať
    const calls = (api.createRequest as ReturnType<typeof vi.fn>).mock.calls;
    expect(calls.length).toBe(0);
    // Prehráva lokálne video recordingu
    expect(comp.clipPhase).toBe('ready');
    expect(comp.clipUrl).toContain('/api/v1/recordings/');
    expect(comp.clipUrl).toContain('/video');
  });

  it('ak nemá recording lokálne video, nastaví phase error', () => {
    // Recording bez recordingId → nie je lokálne video
    api.getDetections = vi.fn(() => of({
      total: 1,
      items: [{ ...detection(1, 5000), recordingId: 999 }], // neexistujúci recording
    }));
    api.getRecordings = vi.fn(() => of([]));
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.selectEvent(0);
    fixture.detectChanges();

    expect(comp.clipPhase).toBe('error');
    expect(comp.clipUrl).toBeNull();
  });

  it('udalosť bez priradeného recordingu nemá video (error)', () => {
    api.getDetections = vi.fn(() => of({
      total: 1,
      items: [{ ...detection(1, 5000), recordingId: 0 }],
    }));
    api.getRecordings = vi.fn(() => of([]));
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.selectEvent(0);
    fixture.detectChanges();

    expect(comp.clipPhase).toBe('error');
  });

  it('prev/next event mení selectedIndex s clampom', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.selectEvent(2);
    comp.nextEvent();
    expect(comp.selectedIndex).toBe(2); // clamp na posledný
    comp.prevEvent();
    expect(comp.selectedIndex).toBe(1);
    comp.prevEvent();
    comp.prevEvent();
    expect(comp.selectedIndex).toBe(0); // clamp na prvý
  });

  it('S22n: onPlayerTime počas prehrávania prepína udalosť podľa pozície videa', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    // Udalosti: 14:00:05, 14:01:00, 14:02:00 (beginTime 14:00:00)
    expect(comp.selectedIndex).toBe(0);

    comp.playing = true;
    comp.onPlayerTime(65); // 14:01:05 → 2. udalosť (14:01:00)
    expect(comp.selectedIndex).toBe(1);

    comp.onPlayerTime(125); // 14:02:05 → 3. udalosť (14:02:00)
    expect(comp.selectedIndex).toBe(2);

    comp.onPlayerTime(200); // za poslednou → ostane posledná
    expect(comp.selectedIndex).toBe(2);
  });

  it('toggle panelov prepína viditeľnosť event panelu a timeline', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    expect(comp.showEvents).toBe(true);
    comp.toggleEvents();
    expect(comp.showEvents).toBe(false);
    comp.toggleEvents();
    expect(comp.showEvents).toBe(true);

    expect(comp.showTimeline).toBe(true);
    comp.toggleTimeline();
    expect(comp.showTimeline).toBe(false);
  });

  it('timeline seek sa prepošle prehrávaču', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    const player = comp.player;
    expect(player).toBeDefined();
    const spy = vi.spyOn(player!, 'seekTo');

    comp.onTimelineSeek(12.5);
    expect(spy).toHaveBeenCalledWith(12.5);
  });

  it('timeUpdate z prehrávača aktualizuje currentTime', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    comp.onPlayerTime(42.5);
    expect(comp.currentTime).toBe(42.5);
  });

  it('udalosti sú zoradené podľa absolútneho času', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    const times = comp.events.map(e => e.absoluteTime.getTime());
    expect([...times].sort((a, b) => a - b)).toEqual(times);
  });

  it('zobrazí zoznam udalostí s časom a typom po slovensky', () => {
    const fixture = createFixture();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Udalosť 1 z 3');
    expect(el.textContent).toContain('pohyb');
    expect(el.textContent).toContain('osoba');
  });

  it('prázdny zoznam udalostí zobrazí prázdny stav', () => {
    api.getDetections = vi.fn(() => of({ total: 0, items: [] }));
    api.getRecordings = vi.fn(() => of([]));
    const fixture = createFixture();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Žiadne udalosti');
  });
});

describe('DashboardComponent (S6-4, recordings/cameras panely, filtre, TODAY)', () => {
  let api: ReturnType<typeof createFakeApi>;

  beforeEach(async () => {
    api = createFakeApi();
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  function createFixture(cameras: CameraDto[] = []) {
    const fixture = TestBed.createComponent(DashboardComponent);
    if (cameras.length > 0) fixture.componentRef.setInput('cameras', cameras);
    fixture.componentRef.setInput('camera', CAMERA);
    fixture.detectChanges();
    return fixture;
  }

  it('recordings panel zobrazí 15-min chunk s časom, počtom a TOTAL v pätičke', () => {
    const fixture = createFixture();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).toContain('Záznamy'); // pill
    // začiatok chunku v lokálnom čase (beginTime záznamu)
    const startLabel = new Date(RECORDING.beginTime).toLocaleTimeString('sk-SK', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
    expect(el.textContent).toContain(startLabel);
    expect(el.textContent).toContain('SPOLU');
    // 3 detekcie dňa → footer ukáže 3
    expect(el.textContent).toContain('3');
  });

  it('klik na záznam v recordings paneli vyberie prvú udalosť daného záznamu', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;

    comp.selectEvent(2); // iná udalosť
    const startLabel = new Date(RECORDING.beginTime).toLocaleTimeString('sk-SK', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
    const row = Array.from(el.querySelectorAll('.rec-row')).find(
      r => (r as HTMLElement).textContent?.includes(startLabel),
    ) as HTMLElement | undefined;
    expect(row).toBeDefined();
    row!.click();
    fixture.detectChanges();

    expect(comp.selectedIndex).toBe(0); // prvá udalosť záznamu (všetky sú z neho)
  });

  it('recordings panel sa dá zavrieť × a prepnúť pillom', () => {
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    expect(comp.showRecordings).toBe(true);
    comp.toggleRecordings();
    expect(comp.showRecordings).toBe(false);
    comp.toggleRecordings();
    expect(comp.showRecordings).toBe(true);
  });

  it('S22g: cameras panel je odstránený (kamery sú v menu vľavo)', () => {
    const fixture = createFixture([CAMERA, CAMERA2]);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.cameras-panel')).toBeNull();
    expect(el.querySelector('.cam-row')).toBeNull();
  });

  it('S22g: auto-výber kamery sa spustí len pri prvom načítaní (bez vybranej kamery)', async () => {
    // Kamera 2 má detekcie, kamera 1 nie → auto-výber vyberie kameru 2
    (api.getDetections as ReturnType<typeof vi.fn>).mockImplementation((params?: { cameraId?: number }) =>
      of({
        total: params?.cameraId === 2 ? 5 : 0,
        items: params?.cameraId === 2 ? [detection(9, 5_000)] : [],
      }),
    );
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.componentRef.setInput('cameras', [CAMERA, CAMERA2]);
    const comp = fixture.componentInstance;
    let emitted: CameraDto | null = null;
    comp.cameraChange.subscribe(c => (emitted = c));

    // camera je null → auto-výber emituje kameru s detekciami (forkJoin je async)
    fixture.detectChanges();
    await new Promise(r => setTimeout(r, 50));
    fixture.detectChanges();
    expect(emitted?.cameraId).toBe(2);
  }, 10_000);

  it('S22g: manuálne vybraná kamera sa po reload rešpektuje (neprepíše ju auto-výber)', () => {
    const fixture = createFixture([CAMERA, CAMERA2]);
    const comp = fixture.componentInstance;
    let emittedCount = 0;
    comp.cameraChange.subscribe(() => emittedCount++);

    // Používateľ vybral kameru 2 (Brána)
    comp.camera = CAMERA2;
    fixture.detectChanges();

    // Auto-výber sa NESMIE spustiť (camera už je vybraná) — žiadny emit navyše
    expect(emittedCount).toBe(0);
  });

  it('⚙ Settings tlačidlo je odstránené z dashboardu (je len v sidebare pre admina)', () => {
    const fixture = createFixture();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.settings-btn')).toBeNull();
  });

  it('filter chip person zobrazí len person udalosti a vyberie prvú z nich', () => {
    api.getDetections = vi.fn(() =>
      of({
        total: 3,
        items: [detection(1, 5_000), detection(2, 60_000, 'person'), detection(3, 120_000)],
      }),
    );
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    expect(comp.filteredEvents.length).toBe(3); // default all
    comp.setFilter('person');
    expect(comp.activeFilter).toBe('person');
    expect(comp.filteredEvents.length).toBe(1);
    expect(comp.filteredEvents[0].detection.detectionType).toBe('person');
    expect(comp.selectedIndex).toBe(0);
  });

  it('filter high intensity zobrazí len udalosti s intenzitou nad prahom (0.08, normalizované 0..1)', () => {
    api.getDetections = vi.fn(() =>
      of({
        total: 2,
        items: [
          detection(1, 5_000, 'motion', { intensity: 0.04 }),
          detection(2, 60_000, 'motion', { intensity: 0.12 }),
        ],
      }),
    );
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.setFilter('high');
    expect(comp.filteredEvents.length).toBe(1);
    expect(comp.filteredEvents[0].detection.detectionId).toBe(2);
  });

  it('filter flagged zobrazí len označené udalosti (flagged aj false_positive)', () => {
    api.getDetections = vi.fn(() =>
      of({
        total: 3,
        items: [
          detection(1, 5_000, 'motion', { flag: 'flagged' }),
          detection(2, 60_000, 'motion', { flag: 'false_positive' }),
          detection(3, 120_000, 'motion', { flag: 'none' }),
        ],
      }),
    );
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    comp.setFilter('flagged');
    expect(comp.filteredEvents.length).toBe(2);
    expect(comp.filteredEvents.map(e => e.detection.detectionId).sort()).toEqual([1, 2]);
  });

  it('filter ovplyvní aj timeline events a event panel', () => {
    api.getDetections = vi.fn(() =>
      of({ total: 2, items: [detection(1, 5_000), detection(2, 60_000, 'person')] }),
    );
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    expect(comp.timelineEvents.length).toBe(2);
    comp.setFilter('person');
    expect(comp.timelineEvents.length).toBe(1);
  });

  it('filter chipy sa renderujú po slovensky (dynamické kľúče)', () => {
    const fixture = createFixture();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.textContent).toContain('Všetko');
    expect(el.textContent).toContain('Osoba');
    expect(el.textContent).toContain('Vozidlo');
    expect(el.textContent).toContain('Vysoká intenzita');
    expect(el.textContent).toContain('Označené');
  });

  it('TODAY stat: celkový počet detekcií cez všetky kamery + 10 stĺpcov', () => {
    api.getRecordings = vi.fn(() => of([todayRecording()]));
    const fixture = createFixture([CAMERA, CAMERA2]);
    const comp = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;

    expect(comp.todayTotal).toBe(6); // 3 (kamera 1) + 3 (kamera 2)
    expect(comp.todayBars.length).toBe(10);
    expect(comp.todayBars.reduce((s, v) => s + v, 0)).toBe(6);
    expect(el.textContent).toContain('POSLEDNÁ HODINA');
    expect(el.textContent).toContain('detekcií');
  });

  it('TODAY stĺpce sa plnia podľa času v poslednej hodine (bucket 10)', () => {
    const today = todayRecording();
    api.getRecordings = vi.fn(() => of([today]));
    api.getDetections = vi.fn(() => of({ total: 1, items: [detection(1, 5_000)] })); // 5 s po beginTime
    const fixture = createFixture();
    const comp = fixture.componentInstance;

    // bucket = decil POSLEDNEJ HODINY (rolling 60 min) do ktorého padne udalosť
    const now = new Date();
    const hourStart = now.getTime() - 60 * 60 * 1000;
    const abs = new Date(today.beginTime).getTime() + 5_000;
    const bucket = Math.min(9, Math.floor(((abs - hourStart) / (60 * 60 * 1000)) * 10));
    expect(comp.todayBars[bucket]).toBe(1);
  });
});
