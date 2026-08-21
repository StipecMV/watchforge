import { describe, it, expect, beforeEach, beforeAll, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { AppComponent } from './app.component';
import { AuthService, AuthUser } from './services/auth.service';
import { ApiService } from './services/api.service';
import { SettingsService } from './services/settings.service';
import { I18nService } from './i18n/i18n.service';

// LiveViewComponent používa window.matchMedia (mobilná detekcia) — mock pre test env.
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
});

const USER: AuthUser = {
  userId: 1,
  username: 'Admin',
  role: 'admin',
  avatarId: 3,
  locale: 'sk',
};

const CAMERA = {
  cameraId: 1,
  nvrId: 1,
  channel: 0,
  friendlyName: 'Dvor',
  iconId: 'garden',
  isActive: true,
};

const STATUS = {
  worker: { queued: 5, running: 2, completed: 44, failed: 1, interrupted: 0, cancelled: 0, total: 52 },
  sync: { backlog: 3, lastSyncUtc: null, totalRecordings: 400, completedAnalyses: 44 },
  apiVersion: '1.0.0',
  serverTimeUtc: '2026-08-14T12:00:00Z',
};

describe('AppComponent verejné UI (live default, admin login, štatistiky)', () => {
  let currentUser: ReturnType<typeof signal<AuthUser | null>>;
  let fakeAuth: AuthService;
  let fakeApi: ApiService;

  beforeEach(async () => {
    currentUser = signal<AuthUser | null>(null);
    fakeAuth = {
      currentUser,
      login: vi.fn(() => of({ kind: 'ok', user: USER })),
      setPassword: vi.fn(() => of({ kind: 'ok', user: USER })),
      logout: vi.fn(() => of(undefined)),
      restoreSession: vi.fn(() => of(null)),
    } as unknown as AuthService;
    fakeApi = {
      getCameras: vi.fn(() => of([CAMERA])),
      getDetections: vi.fn(() => of({ total: 0, items: [] })),
      getRecordings: vi.fn(() => of([])),
      getSystemStatus: vi.fn(() => of(STATUS)),
      createRequest: vi.fn(() => of({ requestId: 1, status: 'queued', estimate: '', clipIds: [], error: null })),
      getRequestStatus: vi.fn(() => of({ requestId: 1, status: 'completed', clipIds: [], error: null })),
      clipUrl: vi.fn((id: number) => `http://localhost:5000/api/v1/clips/${id}`),
    } as unknown as ApiService;

    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        { provide: AuthService, useValue: fakeAuth },
        { provide: ApiService, useValue: fakeApi },
        { provide: SettingsService, useValue: { getUsers: vi.fn(() => of([])), getSystemInfo: vi.fn(() => of({})), getSystemStatus: vi.fn(() => of({ sync: { backlog: 0 } })) } },
      ],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  it('bez prihlásenia zobrazí verejný app shell (live view), NIE login screen', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('app-login')).toBeNull();
    expect(el.querySelector('app-live-view')).not.toBeNull();
    expect(fakeApi.getCameras).toHaveBeenCalled();
  });

  it('default view je LIVE (live stream otvorený ako default)', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    expect(comp.view).toBe('live');
  });

  it('načíta kamery aj bez prihlásenia (verejný endpoint)', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    expect(comp.cameras.length).toBe(1);
    expect(comp.selectedCamera?.cameraId).toBe(1);
  });

  it('výber kamery cez sidebar zmení selectedCamera', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    const other = { ...CAMERA, cameraId: 2, friendlyName: 'Garáž' };
    comp.onCameraSelected(other);
    expect(comp.selectedCamera?.cameraId).toBe(2);
  });

  it('pri chybe načítania kamier zobrazí chybový banner', () => {
    fakeApi.getCameras = vi.fn(() => throwError(() => new Error('network')));
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(compLoadError(fixture.componentInstance)).toBe(true);
    expect(el.textContent).toContain('Nedá sa pripojiť');
  });

  it('načíta verejné štatistiky analyz (koľko beží / čaká)', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    expect(fakeApi.getSystemStatus).toHaveBeenCalled();
    expect(comp.stats?.running).toBe(2);
    expect(comp.stats?.queued).toBe(5);
    expect(comp.stats?.completed).toBe(44);
  });

  it('neadmin nevidí ⚙ Settings — openSettings() je no-op', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    comp.openSettings();
    expect(comp.view).toBe('live'); // ostáva live (settings zablokované pre neadmina)
  });

  it('po admin prihlásení sa odomknú settings', () => {
    currentUser.set(USER);
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    expect(comp.isAdmin).toBe(true);
    comp.openSettings();
    expect(comp.view).toBe('settings');
  });

  it('logout() odhlási admina a vráti na live view', () => {
    currentUser.set(USER);
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    comp.logout();
    expect(comp.isAdmin).toBe(false);
    expect(comp.view).toBe('live');
  });

  it('openDashboard() prepne na analýzy (dashboard)', () => {
    currentUser.set(USER);
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const comp = fixture.componentInstance;

    comp.openDashboard();
    fixture.detectChanges();
    expect(comp.view).toBe('dashboard');
  });
});

function compLoadError(comp: AppComponent): boolean {
  return (comp as unknown as { loadError: boolean }).loadError;
}
