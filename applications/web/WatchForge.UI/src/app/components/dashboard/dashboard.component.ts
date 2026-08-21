import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, interval, map, Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { VideoPlayerComponent } from '../video-player/video-player.component';
import { TimelineComponent } from '../timeline/timeline.component';
import {
  CameraDto,
  DashboardEvent,
  DetectionDto,
  DetectionListDto,
  DetectionRegion,
  EventFilter,
  RecordingDto,
} from '../../models/video.model';

/** Fáza prípravy klipu pre vybranú udalosť (event-first tok). */
export type ClipPhase = 'idle' | 'requesting' | 'processing' | 'ready' | 'error';

const CLIP_CONTEXT_BEFORE_SEC = 15;
const CLIP_CONTEXT_AFTER_SEC = 15;
const POLL_INTERVAL_MS = 2000;
/** Prah pre filter chip „Vysoká intenzita" — intenzita je NORMALIZOVANÁ 0..1 (S22m: max ~0.2). */
const HIGH_INTENSITY_THRESHOLD = 0.08;
/** Počet stĺpcov TODAY mini chartu (design: 10-bar). */
const TODAY_BARS = 10;

/** Dáta jednej kamery za deň (detekcie + záznamy) — zdroj pre panely a TODAY. */
interface CameraDayData {
  camera: CameraDto;
  detections: DetectionDto[];
  /** Presný celkový počet detekcií (API vracia max 2000 items). */
  total?: number;
  recordings: RecordingDto[];
}

/** Riadok recordings panelu: 15-min chunk + počet detekcií v ňom. */
export interface RecordingRow {
  recording: RecordingDto;
  count: number;
}

/**
 * Event-first dashboard (S6-3 + S6-4, FR-11).
 *
 * S6-4 rozšírenia:
 * - 4 panel toggle pily v top bare: Event / Recordings / Cameras / Timeline.
 * - Recordings panel: 15-min chunky vybranej kamery (čas · mini sparkline ·
 *   počet detekcií), pätička TOTAL za deň; klik vyberie prvú udalosť chunku.
 * - Cameras panel: všetky kamery s ikonou, status dot a badge počtu detekcií
 *   dňa; klik prepne kameru (cameraChange → app shell).
 * - TODAY stat card: celkový počet detekcií dňa cez všetky kamery + 10-bar
 *   mini chart rozloženia počas dňa.
 * - Filter chipy: All / Person / Vehicle / High intensity / Flagged —
 *   filtrujú event list, event panel aj timeline.
 *
 * Tok: detekcie dňa (GET /api/v1/detections) + záznamy (recordings) →
 * udalosti s absolútnym časom (beginTime záznamu + timestampMs) →
 * výber udalosti → POST /api/v1/requests (klip) → poll stavu →
 * GET /api/v1/clips/{id} prehrávané vo video playeri.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, VideoPlayerComponent, TimelineComponent, TranslatePipe],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css'],
})
export class DashboardComponent implements OnInit, OnChanges, OnDestroy {
  @ViewChild(VideoPlayerComponent) player?: VideoPlayerComponent;

  @Input() camera: CameraDto | null = null;
  /** Všetky kamery (pre Cameras panel a TODAY stat) — posiela app shell. */
  @Input() cameras: CameraDto[] = [];
  /** Len AKTÍVNE kamery pre Cameras panel (neaktívna CH9 sa nezobrazuje). */
  get activeCameras(): CameraDto[] {
    return this.cameras.filter(c => c.isActive);
  }
  /** Klik na kameru v Cameras paneli — app shell prepne vybranú kameru. */
  @Output() cameraChange = new EventEmitter<CameraDto>();
  /** 🚩 Flag pri udalosti → flag screen (app shell). */
  @Output() flagClick = new EventEmitter<DashboardEvent>();

  events: DashboardEvent[] = [];
  selectedIndex = -1;
  loading = false;
  /** S22g: či sa pre vybranú kameru načítali detekcie (rozlíšenie „analýzy ešte nebežali" vs „žiadne dáta"). */
  eventsLoaded = false;
  loadError = false;
  showEvents = true;
  showRecordings = true;
  showTimeline = true;

  /** Filter chip (S6-4): all | person | vehicle | high | flagged. */
  activeFilter: EventFilter = 'all';

  /** Filter chipy v poradí podľa designu (FR-11). */
  readonly filters: EventFilter[] = ['all', 'person', 'vehicle', 'high', 'flagged'];

  /** Záznamy vybranej kamery za dnešný deň (recordings panel). */
  recordings: RecordingDto[] = [];
  /** Počet detekcií dňa per kamera (cameras panel badge + TODAY). */
  /** Celkový počet detekcií dňa cez všetky kamery (TODAY card). */
  todayTotal = 0;
  /** 10 stĺpcov TODAY mini chartu (počet detekcií na decil dňa). */
  todayBars: number[] = [];

  clipUrl: string | null = null;
  clipPhase: ClipPhase = 'idle';
  clipEstimate = '';
  currentTime = 0;
  duration = 0;
  playing = false;

  // S20: export selekcie (dôkazový klip) — stav + form fieldy
  showExportPanel = false;
  exportFrom = '';
  exportTo = '';
  exportPhase: ClipPhase = 'idle';
  exportClipUrl: string | null = null;
  exportError = '';
  private exportPollSub?: Subscription;
  private activeExportRequestId: number | null = null;

  private pollSub?: Subscription;
  private activeRequestId: number | null = null;
  /** detectionId → clipId cache (opätovný výber nevyrobí nový request). */
  private readonly clipByDetection = new Map<number, number>();
  private cameraDays: CameraDayData[] = [];

  constructor(
    private readonly api: ApiService,
    private readonly i18n: I18nService,
  ) {}

  ngOnInit() {
    // Pri prvom otvorení analýz bez vybranej kamery sa spustí auto-výber
    // (kamery s najviac detekciami) — ngOnChanges by sa nespustil (null → null).
    this.reload();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['camera']) {
      this.reload();
    }
  }

  /** S22l: užívateľ potvrdil kontrolu nahrávky s osobou („skontrolované") → zruší person_pending. */
  markPersonReviewed(): void {
    const rec = this.selected?.recording;
    if (!rec?.recordingId) return;
    this.api.markPersonReviewed(rec.recordingId).subscribe({
      next: () => {
        rec.personPending = false;
        // obnoviť zoznam nahrávok (badge zmizne)
        const row = this.recordingRows.find(r => r.recording.recordingId === rec.recordingId);
        if (row) row.recording.personPending = false;
      },
      error: () => undefined, // ticho — UI sa obnoví pri ďalšom poll
    });
  }

  ngOnDestroy() {
    this.stopPolling();
    this.exportPollSub?.unsubscribe();
  }

  // ---- loading ----

  private todayRange(): { from: string; to: string } {
    // Rolling okno 7 dní (namiesto „len dnešok“): detekcie z minulých dní
    // (analýza historických záznamov) by inak dashboard ukazoval ako 0.
    const now = new Date();
    const from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
    return { from: from.toISOString(), to: now.toISOString() };
  }

  reload() {
    this.stopPolling();
    this.events = [];
    this.recordings = [];
    this.todayTotal = 0;
    this.todayBars = [];
    this.cameraDays = [];
    this.selectedIndex = -1;
    this.clipUrl = null;
    this.clipPhase = 'idle';
    this.clipEstimate = '';
    this.clipByDetection.clear();
    this.currentTime = 0;
    this.duration = 0;
    // Bez vybranej kamery sa načítajú VŠETKY kamery a auto-vyberie sa tá s
    // najviac detekciami (S22g — inak by dashboard bez kamery nič neukázal).
    if (!this.camera && this.cameras.length === 0) return;

    this.loading = true;
    this.loadError = false;
    const { from, to } = this.todayRange();

    // Dáta dňa pre každú kameru (detekcie + záznamy). Bez `cameras` inputu
    // (testy / starší volajúci) sa načítava len vybraná kamera.
    const cams: CameraDto[] = this.cameras.length > 0
      ? this.cameras
      : (this.camera ? [this.camera] : []);
    const perCamera$ = cams.map(c =>
      forkJoin({
        detections: this.api.getDetections({ cameraId: c.cameraId, from, to }),
        recordings: this.api.getRecordings({ cameraId: c.cameraId, from, to }),
      }).pipe(
        map(({ detections, recordings }) => ({ camera: c, detections: detections.items, total: detections.total, recordings })),
      ),
    );

    forkJoin(perCamera$).subscribe({
      next: days => {
        this.cameraDays = days;
        // Auto-výber kamery s NAJVIAC detekciami sa spúšťa LEN pri prvom načítaní
        // (žiadna kamera ešte nevybraná). Pri manuálnom výbere (sidebar klik) sa
        // výber REŠPEKTUJE — inak by dashboard po reload prepísal kameru na tú
        // s najviac detekciami (bug: „klik na 3. kameru → po čase prepne na prvú").
        const selectedId = this.camera?.cameraId
          ?? (days.reduce((b, d) => (d.detections.length > (b?.detections.length ?? -1) ? d : b), null as (typeof days)[number] | null)?.camera.cameraId ?? -1);
        const sel = days.find(d => d.camera.cameraId === selectedId) ?? null;
        // Pri prvom načítaní (user ešte nič nevybral) zosynchronizovať header s auto-výberom.
        if (!this.camera && sel) {
          this.cameraChange.emit(sel.camera);
        }
        this.events = sel ? this.buildEvents(sel.detections, sel.recordings) : [];
        this.eventsLoaded = sel ? sel.detections.length > 0 || sel.recordings.length > 0 : false;
        this.recordings = sel ? [...sel.recordings].sort((a, b) =>
          new Date(a.beginTime).getTime() - new Date(b.beginTime).getTime()) : [];
        this.todayTotal = days.reduce((sum, d) => sum + (d.total ?? d.detections.length), 0);
        this.todayBars = this.buildTodayBars(days);
        this.loading = false;
        if (this.filteredEvents.length > 0) {
          this.selectEvent(0);
        } else {
          this.selectedIndex = -1;
          this.clipPhase = 'idle';
        }
      },
      error: () => {
        this.loading = false;
        this.loadError = true;
      },
    });
  }

  private buildEvents(detections: DetectionDto[], recordings: RecordingDto[]): DashboardEvent[] {
    const recById = new Map(recordings.map(r => [r.recordingId, r]));
    return detections
      .map(d => {
        const recording = recById.get(d.recordingId) ?? null;
        const recStartMs = recording ? new Date(recording.beginTime).getTime() : 0;
        return {
          detection: d,
          recording,
          absoluteTime: new Date(recStartMs + d.timestampMs),
          event: {
            timestampMs: d.timestampMs,
            durationMs: d.durationMs,
            regions: [
              {
                x: d.regionX,
                y: d.regionY,
                width: d.regionW,
                height: d.regionH,
                intensity: d.intensity,
              },
            ],
          },
        };
      })
      .sort((a, b) => a.absoluteTime.getTime() - b.absoluteTime.getTime());
  }

  /** TODAY mini chart: detekcie rozdelené do 10 decilov POSLEDNEJ HODINY (rolling 60 min). */
  private buildTodayBars(days: CameraDayData[]): number[] {
    const now = new Date();
    const hourStart = now.getTime() - 60 * 60 * 1000;
    const hourLen = 60 * 60 * 1000;
    const bars = new Array<number>(TODAY_BARS).fill(0);
    for (const day of days) {
      const recById = new Map(day.recordings.map(r => [r.recordingId, r]));
      for (const d of day.detections) {
        const rec = recById.get(d.recordingId);
        if (!rec) continue;
        const abs = new Date(rec.beginTime).getTime() + d.timestampMs;
        if (abs < hourStart || abs >= hourStart + hourLen) continue;
        const bucket = Math.min(TODAY_BARS - 1, Math.floor(((abs - hourStart) / hourLen) * TODAY_BARS));
        bars[bucket]++;
      }
    }
    return bars;
  }

  // ---- event selection + clip request (event-first) ----

  /** Udalosti po aplikovaní aktívneho filtra (event panel, timeline, výber). */
  get filteredEvents(): DashboardEvent[] {
    switch (this.activeFilter) {
      case 'person':
        return this.events.filter(e => e.detection.detectionType === 'person');
      case 'vehicle':
        return this.events.filter(e => e.detection.detectionType === 'vehicle');
      case 'high':
        return this.events.filter(e => e.detection.intensity >= HIGH_INTENSITY_THRESHOLD);
      case 'flagged':
        return this.events.filter(e => e.detection.flag !== 'none');
      default:
        return this.events;
    }
  }

  get selected(): DashboardEvent | null {
    return this.selectedIndex >= 0 && this.selectedIndex < this.filteredEvents.length
      ? this.filteredEvents[this.selectedIndex]
      : null;
  }

  get region(): DetectionRegion | null {
    const ev = this.selected;
    if (!ev) return null;
    const r = ev.event.regions[0];
    return r ? { x: r.x, y: r.y, width: r.width, height: r.height, intensity: r.intensity } : null;
  }

  get eventLabel(): string {
    const ev = this.selected;
    if (!ev) return '';
    return `${this.i18n.translate('dash.eventOf', { index: this.selectedIndex + 1, total: this.filteredEvents.length })} · ${this.formatEventTime(ev)}`;
  }

  get timelineEvents() {
    return this.filteredEvents.map(e => e.event);
  }

  selectEvent(index: number) {
    if (index < 0 || index >= this.filteredEvents.length) return;
    this.selectedIndex = index;
    const ev = this.filteredEvents[index];

    // S22l: prehrávame LOKÁLNE video nahrávky (žiadny trigger sťahovania).
    // Video po analýze ostáva v tmp pre rolling okná; ak nie je (404), hľadáme
    // cache clip, inak ukážeme „video nie je dostupné".
    const cachedClip = this.clipByDetection.get(ev.detection.detectionId);
    if (cachedClip !== undefined) {
      this.clipUrl = this.api.clipUrl(cachedClip);
      this.clipPhase = 'ready';
      return;
    }
    if (ev.recording?.recordingId) {
      this.clipUrl = this.api.recordingVideoUrl(ev.recording.recordingId);
      this.clipPhase = 'ready';
      return;
    }
    this.clipUrl = null;
    this.clipPhase = 'error';
  }

  /** Prepne filter chip a vyberie prvú udalosť nového zoznamu (S6-4). */
  setFilter(filter: EventFilter) {
    if (this.activeFilter === filter) return;
    this.activeFilter = filter;
    this.selectedIndex = -1;
    if (this.filteredEvents.length > 0) {
      this.selectEvent(0);
    } else {
      this.clipPhase = 'idle';
    }
  }

  private requestClip(ev: DashboardEvent) {
    this.stopPolling();
    this.clipUrl = null;
    this.clipPhase = 'requesting';
    this.activeRequestId = null;

    const absMs = ev.absoluteTime.getTime();
    const durMs = Math.max(1, ev.detection.durationMs);
    const fromTime = new Date(absMs - CLIP_CONTEXT_BEFORE_SEC * 1000).toISOString();
    const toTime = new Date(absMs + durMs + CLIP_CONTEXT_AFTER_SEC * 1000).toISOString();

    this.api
      .createRequest({
        fromTime,
        toTime,
        cameraId: this.camera?.cameraId ?? ev.detection.cameraId,
      })
      .subscribe({
        next: status => {
          this.activeRequestId = status.requestId;
          this.clipPhase = 'processing';
          this.clipEstimate = status.estimate;
          this.startPolling(status.requestId);
        },
        error: () => {
          this.clipPhase = 'error';
        },
      });
  }

  private startPolling(requestId: number) {
    this.pollRequest(requestId);
    this.pollSub = interval(POLL_INTERVAL_MS).subscribe(() => this.pollRequest(requestId));
  }

  /** Jedno poll kolo — public pre testy (volá sa aj z intervalu). */
  pollRequest(requestId: number) {
    if (this.activeRequestId !== requestId) return;
    this.api.getRequestStatus(requestId).subscribe({
      next: status => {
        if (status.status === 'completed' && status.clipIds.length > 0) {
          this.stopPolling();
          const clipId = status.clipIds[0];
          const ev = this.selected;
          if (ev) this.clipByDetection.set(ev.detection.detectionId, clipId);
          this.clipUrl = this.api.clipUrl(clipId);
          this.clipPhase = 'ready';
        } else if (status.status === 'failed' || status.status === 'priority_missed') {
          this.stopPolling();
          this.clipPhase = 'error';
        }
      },
      error: () => {
        this.stopPolling();
        this.clipPhase = 'error';
      },
    });
  }

  private stopPolling() {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }

  // ---- S20: export selekcie (dôkazový klip) ----

  /** Otvorí/zavrie export panel; pri otvorení predvyplní posledné 3 minúty. */
  toggleExportPanel() {
    this.showExportPanel = !this.showExportPanel;
    if (this.showExportPanel && !this.exportFrom) {
      const now = new Date();
      const from = new Date(now.getTime() - 3 * 60 * 1000);
      this.exportFrom = this.toLocalInput(from);
      this.exportTo = this.toLocalInput(now);
    }
  }

  /** datetime-local value („YYYY-MM-DDTHH:mm") z Date. */
  private toLocalInput(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  /** POST /api/v1/exports + poll stavu → clip URL. */
  startExport() {
    if (!this.camera) return;
    const from = new Date(this.exportFrom);
    const to = new Date(this.exportTo);
    if (isNaN(from.getTime()) || isNaN(to.getTime()) || to <= from) {
      this.exportError = 'export.invalidRange';
      return;
    }

    this.exportError = '';
    this.exportClipUrl = null;
    this.exportPhase = 'requesting';
    this.activeExportRequestId = null;
    this.exportPollSub?.unsubscribe();

    this.api
      .createExport({
        fromTime: from.toISOString(),
        toTime: to.toISOString(),
        cameraId: this.camera.cameraId,
      })
      .subscribe({
        next: status => {
          this.activeExportRequestId = status.requestId;
          this.exportPhase = 'processing';
          this.pollExport(status.requestId);
          this.exportPollSub = interval(POLL_INTERVAL_MS).subscribe(() =>
            this.pollExport(status.requestId),
          );
        },
        error: () => {
          this.exportPhase = 'error';
          this.exportError = 'export.failed';
        },
      });
  }

  /** Jedno poll kolo exportu — public pre testy. */
  pollExport(requestId: number) {
    if (this.activeExportRequestId !== requestId) return;
    this.api.getRequestStatus(requestId).subscribe({
      next: status => {
        if (status.status === 'completed' && status.clipIds.length > 0) {
          this.exportPollSub?.unsubscribe();
          this.exportPhase = 'ready';
          this.exportClipUrl = this.api.clipUrl(status.clipIds[0]);
        } else if (status.status === 'failed' || status.status === 'priority_missed') {
          this.exportPollSub?.unsubscribe();
          this.exportPhase = 'error';
          this.exportError = 'export.failed';
        }
      },
      error: () => {
        this.exportPollSub?.unsubscribe();
        this.exportPhase = 'error';
        this.exportError = 'export.failed';
      },
    });
  }

  /** Text stavu exportu (polling/proces). */
  exportStatusText(): string {
    if (this.exportPhase === 'requesting') return this.i18n.translate('export.requesting');
    if (this.exportPhase === 'processing') return this.i18n.translate('export.processing');
    return '';
  }

  // ---- navigation (FR-11: `<<`/`>>` a Prev/Next skákanie medzi udalosťami) ----

  prevEvent() {
    this.selectEvent(this.selectedIndex - 1);
  }

  nextEvent() {
    this.selectEvent(this.selectedIndex + 1);
  }

  // ---- panels + timeline ----

  toggleEvents() {
    this.showEvents = !this.showEvents;
  }

  toggleRecordings() {
    this.showRecordings = !this.showRecordings;
  }

  toggleTimeline() {
    this.showTimeline = !this.showTimeline;
  }

  /** Klik na timeline = seek v prehrávači (FR-11). */
  onTimelineSeek(time: number) {
    this.player?.seekTo(time);
  }

  onPlayerTime(time: number) {
    this.currentTime = time;
    // S22n: auto-sync udalostí s prehrávaním — keď video beží, prepína sa na
    // udalosť zodpovedajúcu aktuálnej pozícii (nie ostáva na prvej). Čas videa
    // 0 = začiatok lokálneho .mp4 (recording.beginTime alebo okno), udalosti sú
    // chronologické podľa absoluteTime.
    if (this.playing && this.filteredEvents.length > 0) {
      this.syncSelectionToPlayback(time);
    }
  }

  /** S22n: nájde udalosť pre aktuálnu pozíciu videa a aktualizuje selectedIndex (bez prepnutia videa). */
  private syncSelectionToPlayback(time: number) {
    const ev = this.selected;
    // Anchor = začiatok videa: windowStartUtc (trimnuté okno) alebo recording.beginTime
    // (celý segment). Player time 0 = anchor.
    const anchorMs = ev?.recording
      ? (ev.recording.windowStartUtc
          ? new Date(ev.recording.windowStartUtc).getTime()
          : new Date(ev.recording.beginTime).getTime())
      : (this.filteredEvents[0]?.absoluteTime.getTime() ?? 0);
    const targetAbs = anchorMs + time * 1000;

    // Binárne hľadanie poslednej udalosti s absoluteTime <= targetAbs („naposledy prehraté")
    let lo = 0, hi = this.filteredEvents.length - 1, best = 0;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (this.filteredEvents[mid].absoluteTime.getTime() <= targetAbs) {
        best = mid;
        lo = mid + 1;
      } else {
        hi = mid - 1;
      }
    }
    if (best !== this.selectedIndex) {
      this.selectedIndex = best;
    }
  }

  onPlayerDuration(duration: number) {
    this.duration = duration;
  }

  onPlayerPlayState(playing: boolean) {
    this.playing = playing;
  }

  // ---- flag screen (S6-7) ----

  /** Otvorí flag screen pre vybranú udalosť (FR-16). */
  openFlag() {
    const ev = this.selected;
    if (ev) this.flagClick.emit(ev);
  }

  // ---- recordings panel (S6-4) ----

  /** Riadky recordings panelu: chunky vybranej kamery + počet detekcií. */
  get recordingRows(): RecordingRow[] {
    const counts = new Map<number, number>();
    for (const ev of this.events) {
      const recId = ev.recording?.recordingId;
      if (recId === undefined || recId === null) continue;
      counts.set(recId, (counts.get(recId) ?? 0) + 1);
    }
    return this.recordings.map(r => ({
      recording: r,
      count: counts.get(r.recordingId) ?? 0,
    }));
  }

  get totalRecordingsDetections(): number {
    return this.recordingRows.reduce((sum, r) => sum + r.count, 0);
  }

  get activeRecordingId(): number | null {
    return this.selected?.recording?.recordingId ?? null;
  }

  /** Mini sparkline (8 stĺpcov): počet aktívnych stĺpcov podľa hustoty. */
  sparklineBars(row: RecordingRow): boolean[] {
    const active = row.count === 0 ? 0 : Math.min(8, Math.max(1, Math.round(row.count / 15)));
    return Array.from({ length: 8 }, (_, i) => i < active);
  }

  /** Trieda vrcholu sparkline podľa počtu detekcií (high/med/low). */
  sparklinePeak(row: RecordingRow): string {
    if (row.count >= 100) return 'high';
    if (row.count >= 30) return 'med';
    if (row.count > 0) return 'low';
    return 'none';
  }

  /** Klik na chunk = výber prvej udalosti z tohto záznamu (ak existuje). */
  selectRecording(recordingId: number) {
    const idx = this.filteredEvents.findIndex(e => e.recording?.recordingId === recordingId);
    if (idx >= 0) this.selectEvent(idx);
  }

  // ---- cameras (S22g: panel odstránený — kamery sú v menu vľavo) ----

  selectCamera(camera: CameraDto) {
    this.cameraChange.emit(camera);
  }

  // ---- TODAY card (S6-4) ----

  /** Výška stĺpca mini chartu (0..100 %) — najvyšší stĺpec = 100 %. */
  barHeight(value: number): number {
    const max = Math.max(1, ...this.todayBars);
    return Math.round((value / max) * 100);
  }

  /** Najvyššia hodnota TODAY chartu (pre zvýraznenie hot stĺpca). */
  get maxBar(): number {
    return this.todayBars.reduce((m, x) => Math.max(m, x), 0);
  }

  // ---- formatters ----

  formatEventTime(ev: DashboardEvent): string {
    return ev.absoluteTime.toLocaleTimeString('sk-SK', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  }

  recordingStartLabel(row: RecordingRow): string {
    return new Date(row.recording.beginTime).toLocaleTimeString('sk-SK', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }

  detectionTypeLabel(type: string): string {
    switch (type) {
      case 'person': return this.i18n.translate('dash.type.person');
      case 'vehicle': return this.i18n.translate('dash.type.vehicle');
      case 'animal': return this.i18n.translate('dash.type.animal');
      case 'face': return this.i18n.translate('dash.type.face');
      default: return this.i18n.translate('dash.type.motion');
    }
  }

  flagLabel(flag: string): string {
    if (flag === 'flagged') return this.i18n.translate('dash.flag.flagged');
    if (flag === 'false_positive') return this.i18n.translate('dash.flag.false_positive');
    return this.i18n.translate('dash.flag.none');
  }

  clipStatusText(): string {
    if (this.clipPhase === 'requesting') return this.i18n.translate('dash.requesting');
    return this.i18n.translate('dash.processing', { estimate: this.clipEstimate });
  }
}
