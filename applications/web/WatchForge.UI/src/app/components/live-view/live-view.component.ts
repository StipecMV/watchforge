import { Component, EventEmitter, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CameraDto } from '../../models/video.model';
import { API_BASE_URL } from '../../services/api.service';
import { TranslatePipe } from '../../i18n/translate.pipe';

/** Režim mriežky live view: 1 (jedna kamera) / 8 (všetky). */
export type LiveGridMode = 1 | 8;

/** FPS podľa režimu (snapshot polling — NVR upload je úzky, MJPEG stream ho nestíha).
 * S22o: view 1 = 10 fps (1080p), view 8 = 1 fps/kamera (360p). */
const VIEW_FPS: Record<LiveGridMode, number> = {
  1: 10,   // jedna kamera — 10 fps (1080p)
  8: 1,    // všetkých 8 — 1 fps / kameru (360p)
};

/** Šírka (px) streamu podľa režimu — view 1 = 1080p (1920), view 8 = 360p (640).
 * S22o: view 8 neúspešne nepotrebuje detaily (stačí 360p), ušetrí upload. */
const VIEW_WIDTH: Record<LiveGridMode, number> = {
  1: 1920, // 1080p (16:9 → 1080 výška)
  8: 640,  // 360p (16:9 → 360 výška)
};

/**
 * S19: Live view — živý obraz z NVR (OPMonitor cez API).
 *
 * Režimy (klik/double-tap na kameru prepína medzi 1 ↔ 8, prepínač ostáva):
 * - view 8 (default): snapshot polling 1 fps / kameru (8 req/s) — všetky kamery
 * - view 1: snapshot polling 15 fps — vybraná kamera
 *
 * Kamery mimo aktívneho view sa UVOĽNIA (POST /release) — nestreamujú na pozadí.
 * Pri zmene režimu sa `loaded` vyčistí → loading spinner, kým neprídu čerstvé framy.
 */
@Component({
  selector: 'app-live-view',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './live-view.component.html',
  styleUrls: ['./live-view.component.css'],
})
export class LiveViewComponent implements OnInit, OnDestroy {
  @Input() cameras: CameraDto[] = [];

  /** Kamera vybraná v sidebare/dashboarde → live view ju zobrazí v režime 1 (S19-4). */
  @Input() set focusCameraId(id: number | null) {
    if (id === null) return;
    if (!this.focusInitialized) {
      this.focusInitialized = true;
      this.selectedCameraId = id;
      return;
    }
    if (id !== this.selectedCameraId) {
      this.selectedCameraId = id;
      this.setGrid(1);
    }
  }
  /** Aktívny režim mriežky — default 8 (všetky kamery). */
  gridMode: LiveGridMode = 8;

  /** Kamera zobrazená v režime 1 (klik/double-tap na bunku ju vyberie). */
  selectedCameraId: number | null = null;
  /** Prvý binding focusCameraId (pri otvorení live view) — uloží, ale NEPrepne na režim 1. */
  private focusInitialized = false;

  /** cameraId → aktuálna frame URL (cache-buster t sa mení pri každom poll). */
  frameUrls = new Map<number, string>();
  /** cameraId → či sa aspoň raz načítal frame (skryje spinner). */
  loaded = new Set<number>();

  private pollTimer?: ReturnType<typeof setInterval>;
  /** Debounce pre rozlíšenie klik vs dvojklik (dvojklik prepína 1↔8). */
  private clickTimer?: ReturnType<typeof setTimeout>;
  private lastDblClick = 0;

  // S22o: client-side zoom + pan (view 1) — wheel (laptop) / pinch (mobil), CSS transform
  zoom = 1;
  panX = 0;
  panY = 0;
  private pinchStartDist = 0;
  private pinchStartZoom = 1;
  /** UI ovládanie zoomu — hovorí, či treba zobraziť +/− (view 1 má gestá aj tlačidlá). */
  get zoomActive(): boolean { return this.gridMode === 1; }

  get activeCameras(): CameraDto[] {
    return this.cameras.filter(c => c.isActive);
  }

  /** Kamery zobrazené v mriežke (režim 1 = vybraná, 8 = všetky). */
  get visibleCameras(): CameraDto[] {
    const active = this.activeCameras;
    // Pri prvom renderi vyplniť frame URL
    for (const cam of active) {
      if (!this.frameUrls.has(cam.cameraId)) {
        this.frameUrls.set(cam.cameraId, this.frameUrl(cam));
      }
    }
    if (!this.pollTimer && active.length > 0) {
      this.startPolling();
    }
    if (this.gridMode === 1) {
      const selected = active.find(c => c.cameraId === this.selectedCameraId) ?? active[0] ?? null;
      return selected ? [selected] : [];
    }
    return active.slice(0, 8);
  }

  /**
   * Klik na kameru: single click → režim 1 s kamerou (fokus); dvojklik (tap) →
   * prepína medzi 1 ↔ 8 (dvojklik na kameru prepína, prepínač 1/8 ostáva).
   */
  onCellClick(camera: CameraDto) {
    // Dvojklik: druhý click prichádza do ~250 ms — ak prišiel dblclick, preskočiť
    if (Date.now() - this.lastDblClick < 350) return;
    if (this.clickTimer) clearTimeout(this.clickTimer);
    this.clickTimer = setTimeout(() => {
      this.selectCamera(camera);
    }, 260);
  }

  /** Dvojklik/tap na kameru → prepína medzi režimami 1 a 8. */
  onCellDblClick(camera: CameraDto) {
    this.lastDblClick = Date.now();
    if (this.clickTimer) clearTimeout(this.clickTimer);
    if (this.gridMode === 1) {
      // Z režimu 1 späť na 8 (všetky kamery)
      this.setGrid(8);
    } else {
      // Z režimu 8 na 1 s touto kamerou
      this.selectedCameraId = camera.cameraId;
      this.setGrid(1);
    }
  }

  /** Klik (single) → zobrazí kameru v režime 1. */
  selectCamera(camera: CameraDto) {
    this.selectedCameraId = camera.cameraId;
    this.setGrid(1);
  }

  /** Prechod medzi režimami 1/8 — vyčistí staré framy (loading) a uvoľní kamery mimo view. */
  setGrid(mode: LiveGridMode) {
    if (mode === this.gridMode) return;
    this.gridMode = mode;
    // S22o: zoom/pan platí len pre view 1 — pri prepnutí reset
    this.resetZoom();
    // Loading: staré framy nesmú ostať — kým neprídu čerstvé, ukazuje sa spinner
    this.loaded.clear();
    // Kamery mimo nového view uvoľniť (nestreamujú na pozadí — šetrí NVR upload/CPU)
    this.releaseInvisible();
    this.restartPolling();
    // Pri zmene režimu obnoviť URL viditeľných (nový interval/fps)
    for (const cam of this.visibleCameras) {
      this.frameUrls.set(cam.cameraId, this.frameUrl(cam));
    }
  }

  // ── S22o: client-side zoom + pan (view 1) ─────────────────────────────────

  /** Reset zoom/pan (pri zmene view). */
  resetZoom() {
    this.zoom = 1;
    this.panX = 0;
    this.panY = 0;
  }

  /** Zoom tlačidlami +/− (prístupnosť; aj gestá nižšie). */
  zoomBy(factor: number) {
    const next = Math.min(8, Math.max(1, this.zoom * factor));
    this.zoom = round2(next);
  }

  /** Wheel zoom (laptop): kolečko približuje/odďaľuje. */
  onWheel(event: WheelEvent) {
    if (this.gridMode !== 1) return;
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15;
    const prev = this.zoom;
    this.zoom = round2(Math.min(8, Math.max(1, this.zoom * factor)));
    // Pan okolo kurzora (priblížiť na to, kam ukazuje myš)
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const mx = (event.clientX - rect.left) / rect.width;
    const my = (event.clientY - rect.top) / rect.height;
    const ratio = this.zoom / prev;
    // Kompenzácia bodu, aby zoom centrový na kurzor (limit na image hranice)
    this.panX = clampPan(mx - (mx - this.panX) * ratio, this.zoom);
    this.panY = clampPan(my - (my - this.panY) * ratio, this.zoom);
  }

  /** Pinch (mobil): dva prsty — približuje/odďaľuje, posun pan. */
  onTouchStart(event: TouchEvent) {
    if (this.gridMode !== 1) return;
    if (event.touches.length === 2) {
      event.preventDefault();
      this.pinchStartDist = touchDist(event);
      this.pinchStartZoom = this.zoom;
    }
  }

  onTouchMove(event: TouchEvent) {
    if (this.gridMode !== 1) return;
    if (event.touches.length === 2) {
      event.preventDefault();
      const dist = touchDist(event);
      if (this.pinchStartDist > 0) {
        const prev = this.zoom;
        this.zoom = round2(Math.min(8, Math.max(1, this.pinchStartZoom * (dist / this.pinchStartDist))));
        // Jednoduchý pan: posun stredovej vzdialenosti sa premieta do pan
        const dx = event.touches[0].clientX - event.touches[1].clientX;
        const dy = event.touches[0].clientY - event.touches[1].clientY;
        const cx = (event.touches[0].clientX + event.touches[1].clientX) / 2;
        const cy = (event.touches[0].clientY + event.touches[1].clientY) / 2;
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        const nx = (cx - rect.left) / rect.width;
        const ny = (cy - rect.top) / rect.height;
        const ratio = this.zoom / prev;
        this.panX = clampPan(nx - (nx - this.panX) * ratio, this.zoom);
        this.panY = clampPan(ny - (ny - this.panY) * ratio, this.zoom);
        void dx; void dy;
      }
    }
  }

  /** Frame URL pre kameru (snapshot polling) — S22o: ?w=&amp;fps= podľa režimu (view 1 = 1080p/10fps, view 8 = 360p/1fps). */
  private frameUrl(camera: CameraDto): string {
    const w = VIEW_WIDTH[this.gridMode];
    const fps = VIEW_FPS[this.gridMode];
    return `${API_BASE_URL}/api/v1/live/${camera.cameraId}/frame?w=${w}&fps=${fps}&t=${Date.now()}`;
  }

  /** Release URL pre kameru v aktuálnom režime (S22o: uvoľní len ten režim). */
  private releaseUrl(camera: CameraDto): string {
    const w = VIEW_WIDTH[this.gridMode];
    const fps = VIEW_FPS[this.gridMode];
    return `${API_BASE_URL}/api/v1/live/${camera.cameraId}/release?w=${w}&fps=${fps}`;
  }

  /** Polling interval podľa režimu (ms): 8 → 1000 ms (1 fps), 1 → 66 ms (15 fps). */
  private pollIntervalMs(): number {
    return Math.round(1000 / VIEW_FPS[this.gridMode]);
  }

  /** Polling: obnovuje len VIDITEĽNÉ kamery (nie všetky — kamery na pozadí nestreamujú). */
  private startPolling() {
    const tick = () => {
      const visible = this.visibleCameras;
      for (const cam of visible) {
        this.frameUrls.set(cam.cameraId, this.frameUrl(cam));
      }
    };
    // pollTimer nastaviť PRED prvým tickom — inak by getter visibleCameras
    // (ktorý spúšťa startPolling pri !pollTimer) spôsobil nekonečnú rekurziu.
    this.pollTimer = setInterval(tick, this.pollIntervalMs());
    tick();
  }

  /** Zruší a spustí polling odznova (pri zmene režimu). */
  private restartPolling() {
    if (this.pollTimer) { clearInterval(this.pollTimer); this.pollTimer = undefined; }
    if (this.activeCameras.length > 0) this.startPolling();
  }

  /** Uvoľní streamy kamier, ktoré nie sú v aktívnom view (fire-and-forget POST /release). */
  private releaseInvisible() {
    const visible = new Set(this.visibleCameras.map(c => c.cameraId));
    for (const cam of this.activeCameras) {
      if (!visible.has(cam.cameraId)) {
        fetch(this.releaseUrl(cam), { method: 'POST', credentials: 'include' }).catch(() => {});
        // Vyčistiť URL z mapy — pri návrate sa nastaví čerstvá
        this.frameUrls.delete(cam.cameraId);
        this.loaded.delete(cam.cameraId);
      }
    }
  }

  /** Ďalšie prázdne bunky mriežky (8 buniek — bez prázdneho 9. slotu). */
  get gridSlots(): number[] {
    const count = this.gridMode === 1 ? 1 : 8;
    return Array.from({ length: count }, (_, i) => i);
  }

  /** Frame sa načítal → skry spinner. */
  onFrameLoaded(cameraId: number) {
    this.loaded.add(cameraId);
  }

  /** Frame zlyhal (NVR timeout/sieť) — ďalší poll skúsi znova (žiadny fallback). */
  onFrameError(_event: Event) {
    /* polling pokračuje — ďalší pokus príde v ďalšom ticku */
  }

  ngOnInit() {}

  ngOnDestroy() {
    if (this.pollTimer) clearInterval(this.pollTimer);
    if (this.clickTimer) clearTimeout(this.clickTimer);
    // Odchod z live view — uvoľniť všetky streamy (UI ich už nezobrazuje)
    for (const cam of this.activeCameras) {
      fetch(this.releaseUrl(cam), { method: 'POST', credentials: 'include' }).catch(() => {});
    }
  }
}

// ── S22o: helpery pre zoom/pan (modulové, TDD-testovateľné) ───────────────────

/** Zaokrúhli zoom na 2 desatinné miesta (stabilné CSS transform). */
function round2(v: number): number {
  return Math.round(v * 100) / 100;
}

/** Vzdialenosť dvoch prstov pre pinch (px). */
function touchDist(e: TouchEvent): number {
  if (e.touches.length < 2) return 0;
  const dx = e.touches[0].clientX - e.touches[1].clientX;
  const dy = e.touches[0].clientY - e.touches[1].clientY;
  return Math.sqrt(dx * dx + dy * dy);
}

/** Obmedzí pan tak, aby obraz neodišiel mimo viewport pri danom zoome
 * (normalizovaná súradnica 0..1; útek je (zoom-1)/2 na každú stranu). */
function clampPan(p: number, zoom: number): number {
  const limit = (zoom - 1) / 2;
  return Math.max(-limit, Math.min(limit, p));
}
