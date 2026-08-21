import { Component, OnInit, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from './services/api.service';
import { AuthService } from './services/auth.service';
import { CameraDto, DashboardEvent } from './models/video.model';
import { SidebarComponent } from './components/sidebar/sidebar.component';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { SettingsComponent } from './components/settings/settings.component';
import { FlagScreenComponent } from './components/flag-screen/flag-screen.component';
import { LiveViewComponent } from './components/live-view/live-view.component';
import { LoginComponent } from './components/login/login.component';
import { TranslatePipe } from './i18n/translate.pipe';

/** Hlavné obrazovky app shellu: live (default) ↔ dashboard (analýzy) + settings (admin) + flag. */
export type AppView = 'dashboard' | 'settings' | 'flag' | 'live' | 'login';

/** Štatistiky analyz (verejné — GET /system/status). */
export interface AnalysisStats {
  running: number;
  queued: number;
  completed: number;
  failed: number;
  total: number;
  /** Synchronizácia: backlog (čakajúce záznamy), celkovo, hotové analýzy, posledný sync. */
  backlog: number;
  totalRecordings: number;
  completedAnalyses: number;
  lastSyncUtc: string | null;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    SidebarComponent,
    DashboardComponent,
    SettingsComponent,
    FlagScreenComponent,
    LiveViewComponent,
    LoginComponent,
    TranslatePipe,
  ],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css'],
})
export class AppComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);

  cameras: CameraDto[] = [];
  selectedCamera: CameraDto | null = null;
  loadError = false;
  restoring = true;
  /** Aktívna obrazovka — LIVE je default (S19): prihlásenie netreba, UI je verejné. */
  view: AppView = 'live';
  /** Udalosť otvorená na flag screen (S6-7). */
  flagEvent: DashboardEvent | null = null;
  /** Mobilný drawer sidebar (otvorený/zatvorený). */
  mobileSidebarOpen = false;
  /** Štatistiky analyz (verejné, GET /system/status) — koľko beží / čaká. */
  stats: AnalysisStats | null = null;
  /** S22j: analýzy zapnuté/vypnuté (WatchForge__Api__Features__AnalysesEnabled). */
  analysesEnabled = true;
  /** Je admin prihlásený? (settings + admin menu vidí len admin). */
  isAdmin = false;
  private camerasLoaded = false;

  constructor() {
    // Verejné UI: live view aj analýzy fungujú BEZ prihlásenia. Prihlásený
    // admin dostane navyše Settings (users, roly, kamery, systém).
    effect(() => {
      const user = this.auth.currentUser();
      this.isAdmin = user?.role === 'admin';
      if (user && !this.camerasLoaded) {
        this.camerasLoaded = true;
        this.loadCameras();
      }
    });
  }

  ngOnInit() {
    // Kamery načítať vždy (verejný endpoint) — UI funguje bez login screenu.
    this.loadCameras();
    this.loadStats();
    // Obnovenie session cookie pri štarte — ak je admin prihlásený, ostane prihlásený.
    this.auth.restoreSession().subscribe({
      complete: () => (this.restoring = false),
    });
    // /admin stránka: admin sa prihlási cez URL hash #/admin (žiadny router —
    // SPA, hash funguje aj cez ui-server.py bez server-side routing).
    this.checkAdminHash();
    window.addEventListener('hashchange', () => this.checkAdminHash());
    // Pravidelné obnovenie štatistík (koľko analyz beží / čaká).
    setInterval(() => this.loadStats(), 30_000);
  }

  /** #/admin v URL → zobraziť admin login (ak ešte nie je prihlásený admin). */
  private checkAdminHash() {
    if (window.location.hash.startsWith('#/admin') && !this.isAdmin) {
      this.view = 'login';
    }
  }

  /** Načíta kamery (GET /api/v1/cameras — verejné) a vyberie prvú aktívnu. */
  loadCameras() {
    this.loadError = false;
    this.api.getCameras().subscribe({
      next: data => {
        this.cameras = data;
        const first = data.find(c => c.isActive) ?? data[0] ?? null;
        if (first && !this.selectedCamera) this.selectedCamera = first;
      },
      error: () => {
        this.loadError = true;
      },
    });
  }

  /** Načíta štatistiky analyz (GET /api/v1/system/status — verejné). */
  loadStats() {
    this.api.getSystemStatus().subscribe({
      next: status => {
        this.analysesEnabled = status.analysesEnabled;
        this.stats = {
          running: status.worker.running,
          queued: status.worker.queued,
          completed: status.worker.completed,
          failed: status.worker.failed,
          total: status.worker.total,
          backlog: status.sync.backlog,
          totalRecordings: status.sync.totalRecordings,
          completedAnalyses: status.sync.completedAnalyses,
          lastSyncUtc: status.sync.lastSyncUtc,
        };
      },
      error: () => {
        this.stats = null;
      },
    });
  }

  onCameraSelected(camera: CameraDto) {
    this.selectedCamera = camera;
    // Klik na kameru v sidebare → rovno live view v režime 1 (S19-4).
    if (this.view === 'live') return;
  }

  /** ⚙ v dashboard top bare → Settings (S6-5) — LEN admin. */
  openSettings() {
    if (!this.isAdmin) return;
    this.view = 'settings';
  }

  /** ← Dashboard v Settings top bare → späť na dashboard. */
  openDashboard() {
    this.view = 'dashboard';
  }

  // ── live view (S19) ───────────────────────────────────────────────────

  /** „Live" v sidebar → live view (1/2/4/8 kamier). */
  openLive() {
    this.view = 'live';
  }

  /** „Analýzy" v live view → event-first dashboard. */
  closeLive() {
    this.view = 'dashboard';
  }

  // ── flag screen (S6-7) ────────────────────────────────────────────────

  /** 🚩 Flag pri udalosti → fullscreen flag screen. */
  openFlagScreen(event: DashboardEvent) {
    this.flagEvent = event;
    this.view = 'flag';
  }

  /** Save/Cancel/home na flag screen → späť na dashboard. */
  closeFlagScreen() {
    this.flagEvent = null;
    this.view = 'dashboard';
  }

  /** Názov kamery udalosti pre flag screen (friendlyName alebo CHx). */
  cameraNameOf(event: DashboardEvent | null): string {
    if (!event) return '';
    const cam = this.cameras.find(c => c.cameraId === event.detection.cameraId);
    return cam?.friendlyName || '';
  }

  /** Čas udalosti (top bar flag screenu, „06:14:02"). SVK 24h formát. */
  formatFlagTime(event: DashboardEvent | null): string {
    if (!event) return '';
    return event.absoluteTime.toLocaleTimeString('sk-SK', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  }

  /** Dátum + čas udalosti pre spodný chip flag screenu. SVK: DD.MM.YYYY, 24h. */
  eventDateOf(event: DashboardEvent | null): string {
    if (!event) return '';
    return event.absoluteTime.toLocaleString('sk-SK', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  }

  /** Admin login → po prihlásení live view (ostane). */
  onAdminLoggedIn() {
    this.view = 'live';
  }

  /** Formátovanie času poslednej synchronizácie (ISO → lokálny čas). SVK 24h. */
  formatSyncTime(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleString('sk-SK', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }

  /** Admin odhlásenie → live view (verejné UI ostáva). */
  logout() {
    this.auth.logout().subscribe(() => {
      this.isAdmin = false;
      this.view = 'live';
    });
  }
}
