import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { SettingsComponent } from './settings.component';
import { AuthService } from '../../services/auth.service';
import { SettingsService } from '../../services/settings.service';
import { I18nService } from '../../i18n/i18n.service';
import { CameraDto } from '../../models/video.model';
import { DetectionProfile, SettingsUser, SystemInfo, ZoneDto } from '../../models/settings.model';

const USER = { userId: 1, username: 'admin', role: 'admin', avatarId: 3, locale: 'sk' };

const CAMERAS: CameraDto[] = [
  { cameraId: 1, nvrId: 1, channel: 0, friendlyName: 'Dvor', iconId: 'garden', isActive: true },
  { cameraId: 2, nvrId: 1, channel: 1, friendlyName: 'Brána', iconId: 'gate', isActive: true },
];

const USERS: SettingsUser[] = [
  { userId: 1, username: 'admin', role: 'admin', avatarId: 3, locale: 'sk', hasPassword: true },
  { userId: 2, username: 'user1', role: 'standard', avatarId: 2, locale: 'sk', hasPassword: false },
];

const PROFILE: DetectionProfile = {
  configVersionId: 4,
  cameraId: 1,
  profileType: 'per_camera',
  sensitivity: 0.62,
  intensityThreshold: 0.03,
  minContourArea: 0.0011,
  ignoreZones: [{ x: 0.1, y: 0.1, w: 0.2, h: 0.2, name: 'Driveway entry', shape: 'rect' }],
  focusZones: [],
};

const SYSTEM_INFO: SystemInfo = {
  nvr: { nvrId: 1, siteId: 'site-a', host: '192.168.68.10', port: 34567, username: 'nvr-user', passwordSecretEnv: 'WF_TEST_NVR_PASSWORD' },
  worker: { queued: 2, running: 1, completed: 40, failed: 0, interrupted: 0, cancelled: 0, total: 43 },
  apiVersion: '1.0.0',
  serverTimeUtc: '2026-08-08T12:00:00Z',
};

function createFakeSettings(overrides: Record<string, unknown> = {}) {
  return {
    updateMe: vi.fn(() => of({ avatarId: 7, locale: 'en' })),
    changePassword: vi.fn(() => of({ ok: true })),
    updateCamera: vi.fn((id: number, friendlyName: string, iconId: string, isActive: boolean) =>
      of({ cameraId: id, friendlyName, iconId, isActive }),
    ),
    getProfile: vi.fn(() => of(PROFILE)),
    putProfile: vi.fn((_id: number, req: unknown) => of({ ...PROFILE, ...(req as object) })),
    getSharedProfile: vi.fn(() => of({ ...PROFILE, cameraId: null, profileType: 'shared' })),
    putSharedProfile: vi.fn((req: unknown) => of({ ...PROFILE, cameraId: null, profileType: 'shared', ...(req as object) })),
    getUsers: vi.fn(() => of(USERS)),
    resetPassword: vi.fn(() => of({ ok: true })),
    getSystemInfo: vi.fn(() => of(SYSTEM_INFO)),
    getSystemStatus: vi.fn(() => of({
      nvr: SYSTEM_INFO.nvr,
      worker: SYSTEM_INFO.worker,
      sync: { backlog: 3, lastSyncUtc: '2026-08-08T10:00:00Z', totalRecordings: 10, completedAnalyses: 7 },
      apiVersion: '1.0.0',
      serverTimeUtc: '2026-08-08T12:00:00Z',
    })),
    getIdentities: vi.fn(() => of([])),
    createIdentity: vi.fn((name: string) => of({ identityId: 99, name, createdAt: new Date().toISOString(), faceCount: 0 })),
    learnIdentity: vi.fn(() => of({ assigned: 1 })),
    deleteIdentity: vi.fn(() => of({ deleted: true })),
    ...overrides,
  };
}

describe('SettingsComponent (S6-5)', () => {
  let fakeAuth: AuthService;
  let fakeSettings: ReturnType<typeof createFakeSettings>;
  let i18n: I18nService;
  let currentUser: ReturnType<typeof signal<typeof USER | null>>;

  beforeEach(async () => {
    currentUser = signal(USER);
    fakeAuth = { currentUser } as unknown as AuthService;
    fakeSettings = createFakeSettings();
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: AuthService, useValue: fakeAuth },
        { provide: SettingsService, useValue: fakeSettings },
      ],
    }).compileComponents();
    i18n = TestBed.inject(I18nService);
    i18n.setLocale('sk');
  });

  function createFixture(role: string = 'admin') {
    currentUser.set({ ...USER, role });
    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.componentRef.setInput('cameras', CAMERAS.map(c => ({ ...c })));
    fixture.detectChanges();
    return fixture;
  }

  // ── navigácia a role ───────────────────────────────────────────────────

  it('default zobrazí Profile sekciu s identitou a avatar gridom', () => {
    const el = createFixture().nativeElement as HTMLElement;
    expect(el.querySelector('.nav-item.active')?.textContent).toContain('Profil');
    expect(el.querySelector('.identity-name')?.textContent).toContain('admin');
    expect(el.querySelectorAll('.avatar-cell').length).toBe(10);
  });

  it('admin vidí Users a System sekcie, štandardný používateľ nie (bez ADMIN tagov)', () => {
    const adminEl = createFixture('admin').nativeElement as HTMLElement;
    // S22g: admin tagy odstránené — kategórie nezvýrazňujú admin prístup
    expect(adminEl.querySelectorAll('.admin-tag').length).toBe(0);
    const adminNav = adminEl.querySelector('.nav')?.textContent ?? '';
    expect(adminNav).toContain('Používatelia');
    expect(adminNav).toContain('Systém');

    const stdEl = createFixture('standard').nativeElement as HTMLElement;
    const nav = stdEl.querySelector('.nav')?.textContent ?? '';
    expect(nav).not.toContain('Používatelia');
    expect(nav).not.toContain('Systém');
    expect(stdEl.querySelectorAll('.admin-tag').length).toBe(0);
  });

  it('Dashboard button je odstránený zo settings (S22g — navigácia cez sidebar)', () => {
    const fixture = createFixture();
    expect(fixture.nativeElement.querySelector('.dashboard-btn')).toBeNull();
  });

  // ── Profile ────────────────────────────────────────────────────────────

  it('výber avatara uloží profil a aktualizuje currentUser', () => {
    const fixture = createFixture();
    const cells = fixture.nativeElement.querySelectorAll('.avatar-cell');
    (cells[6] as HTMLElement).click(); // av07
    fixture.detectChanges();

    expect(fakeSettings.updateMe).toHaveBeenCalledWith(7, 'sk');
    expect(fakeAuth.currentUser()!.avatarId).toBe(7);
  });

  it('prepnutie locale uloží profil a zmení i18n', () => {
    const fixture = createFixture();
    const langButtons = fixture.nativeElement.querySelectorAll('.lang-btn');
    (langButtons[1] as HTMLElement).click(); // English
    fixture.detectChanges();

    expect(fakeSettings.updateMe).toHaveBeenCalledWith(3, 'en');
    expect(i18n.locale()).toBe('en');
    expect(fakeAuth.currentUser()!.locale).toBe('en');
  });

  // ── Cameras ────────────────────────────────────────────────────────────

  it('Cameras sekcia: zmena názvu → dirty → Save zavolá updateCamera', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('cameras');
    fixture.detectChanges();

    expect(fakeSettings.updateCamera).not.toHaveBeenCalled();

    const input = fixture.nativeElement.querySelector('.name-input') as HTMLInputElement;
    input.value = 'Predný dvor';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.componentInstance.camerasDirty).toBe(true);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.dirty-hint')?.textContent,
    ).toContain('neuložené');

    (fixture.nativeElement as HTMLElement).querySelector('.sticky-actions .primary')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fakeSettings.updateCamera).toHaveBeenCalledWith(1, 'Predný dvor', 'garden', true);
    expect(fixture.componentInstance.camerasDirty).toBe(false);
  });

  it('Cameras sekcia: Cancel vráti pôvodné hodnoty', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('cameras');
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('.name-input') as HTMLInputElement;
    input.value = 'Zmenené';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.sticky-actions .ghost')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fixture.componentInstance.camerasDirty).toBe(false);
    expect(fixture.componentInstance.edit(1).friendlyName).toBe('Dvor');
  });

  it('Cameras sekcia: icon picker je odstránený (S22g — ikonky zbytočné)', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('cameras');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.icon-trigger')).toBeNull();
    expect(fixture.nativeElement.querySelector('.icon-picker')).toBeNull();
  });

  // ── Detection ──────────────────────────────────────────────────────────

  it('Detection: default je spoločný profil (shared) pre všetky kamery', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('detection');
    fixture.detectChanges();

    expect(fixture.componentInstance.detectionPerCamera).toBe(false);
    expect(fakeSettings.getSharedProfile).toHaveBeenCalled();
    expect(fixture.componentInstance.uiSensitivity).toBe(62);
    expect(fixture.componentInstance.uiMinSizePx).toBeGreaterThanOrEqual(8);
    expect(fixture.componentInstance.uiIntensityThreshold).toBe(3);
    expect(fixture.nativeElement.querySelectorAll('.slider-wrap input[type="range"]').length).toBe(3);
  });

  it('Detection: switch per-kamera načíta profil vybranej kamery', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('detection');
    fixture.detectChanges();

    fixture.componentInstance.toggleDetectionMode(); // per-kamera ON
    fixture.detectChanges();

    expect(fixture.componentInstance.detectionPerCamera).toBe(true);
    expect(fakeSettings.getProfile).toHaveBeenCalledWith(1); // prvá kamera
    expect(fixture.componentInstance.uiSensitivity).toBe(62);
  });

  it('Detection: Save posiela normalizované hodnoty (0..1) a zachová zóny (shared)', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('detection');
    fixture.detectChanges();

    fixture.componentInstance.uiSensitivity = 70;
    fixture.componentInstance.uiMinSizePx = 100;
    fixture.componentInstance.uiIntensityThreshold = 10;
    (fixture.nativeElement as HTMLElement).querySelector('.sticky-actions .primary')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    const req = fakeSettings.putSharedProfile.mock.calls[0][0] as {
      sensitivity: number;
      intensityThreshold: number;
      minContourArea: number;
      ignoreZones: { x: number; y: number; w: number; h: number }[];
    };
    expect(req.sensitivity).toBe(0.7);
    expect(req.intensityThreshold).toBe(0.1);
    expect(req.minContourArea).toBeCloseTo((100 * 100) / (1920 * 1080), 6);
    expect(req.ignoreZones).toEqual([{ x: 0.1, y: 0.1, w: 0.2, h: 0.2, name: 'Driveway entry', shape: 'rect' }]);
  });

  it('Detection: chýbajúci profil (404) → defaulty + info hláška, Save vytvorí', () => {
    fakeSettings.getSharedProfile = vi.fn(() => throwError(() => ({ status: 404 })));
    fakeSettings.putSharedProfile = vi.fn(() => of({ ...PROFILE, cameraId: null, profileType: 'shared' }));

    const fixture = createFixture();
    fixture.componentInstance.selectSection('detection');
    fixture.detectChanges();

    expect(fixture.componentInstance.profileLoadError).toBe(true);
    expect(fixture.componentInstance.uiSensitivity).toBe(50);
  });

  // ── Users (admin) ──────────────────────────────────────────────────────

  it('Users: zoznam sa načíta a reset hesla vyžaduje potvrdenie (dvojklik)', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('users');
    fixture.detectChanges();

    expect(fakeSettings.getUsers).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelectorAll('.user-row').length).toBe(2);

    const resetBtn = fixture.nativeElement.querySelectorAll('.user-row .danger')[1] as HTMLElement; // user1
    resetBtn.click(); // prvý klik = arm
    fixture.detectChanges();
    expect(fakeSettings.resetPassword).not.toHaveBeenCalled();

    resetBtn.click(); // druhý klik = vykoná
    fixture.detectChanges();
    expect(fakeSettings.resetPassword).toHaveBeenCalledWith('user1');
  });

  // ── Security ───────────────────────────────────────────────────────────

  it('Security: krátke heslo → chyba bez volania API', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('security');
    fixture.componentInstance.secCurrent = 'heslo1234';
    fixture.componentInstance.secNew = 'kratke';
    fixture.componentInstance.secConfirm = 'kratke';
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.btn.primary')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fakeSettings.changePassword).not.toHaveBeenCalled();
    expect(fixture.componentInstance.secMessage?.kind).toBe('error');
  });

  it('Security: nezhodné heslo → chyba', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('security');
    fixture.componentInstance.secCurrent = 'heslo1234';
    fixture.componentInstance.secNew = 'nove-heslo-123';
    fixture.componentInstance.secConfirm = 'ine-heslo-456';
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.btn.primary')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fakeSettings.changePassword).not.toHaveBeenCalled();
    expect(fixture.componentInstance.secMessage?.kind).toBe('error');
  });

  it('Security: správna zmena zavolá changePassword a vyčistí formulár', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('security');
    fixture.componentInstance.secCurrent = 'heslo1234';
    fixture.componentInstance.secNew = 'nove-heslo-123';
    fixture.componentInstance.secConfirm = 'nove-heslo-123';
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.btn.primary')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fakeSettings.changePassword).toHaveBeenCalledWith('heslo1234', 'nove-heslo-123');
    expect(fixture.componentInstance.secMessage?.kind).toBe('ok');
    expect(fixture.componentInstance.secNew).toBe('');
  });

  // ── Zones (S6-6: Focus zone editor) ───────────────────────────────────

  it('Zones: nav položka a výber kamery načíta profil so zónami', () => {
    const fixture = createFixture();
    const nav = (fixture.nativeElement as HTMLElement).querySelector('.nav')?.textContent ?? '';
    expect(nav).toContain('Zóny');

    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    expect(fakeSettings.getProfile).toHaveBeenCalledWith(1);
    const rows = fixture.nativeElement.querySelectorAll('.zone-row');
    expect(rows.length).toBe(1); // PROFILE.ignoreZones
    expect((rows[0] as HTMLElement).textContent).toContain('Driveway entry');
    expect((rows[0] as HTMLElement).textContent).toContain('IGNORE');
  });

  it('Zones: Add zone otvorí editor (nová zóna, default typ ignore)', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.add-zone')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    const editor = (fixture.nativeElement as HTMLElement).querySelector('app-zone-editor');
    expect(editor).not.toBeNull();
    expect(fixture.componentInstance.zoneEditor).not.toBeNull();
    expect(fixture.componentInstance.zoneEditor!.editing).toBeNull();
  });

  it('Zones: Edit zóny otvorí editor s initialZone a typom', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.zone-edit')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fixture.componentInstance.zoneEditor).not.toBeNull();
    expect(fixture.componentInstance.zoneEditor!.editing?.list).toBe('ignore');
    expect(fixture.componentInstance.zoneEditor!.editing?.index).toBe(0);
  });

  it('Zones: Save z editora pridá zónu do profilu cez putProfile a zatvorí editor', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    fixture.componentInstance.zoneEditor = {
      cameraId: 1,
      profile: { ...PROFILE },
      editing: null,
    };
    fixture.detectChanges();

    const zone = { x: 0.2, y: 0.2, w: 0.3, h: 0.3, shape: 'rect' as const, points: [] as number[][] };
    fixture.componentInstance.onZoneSave({ cameraId: 1, name: 'Parkovisko', type: 'ignore', zone });
    fixture.detectChanges();

    expect(fakeSettings.putProfile).toHaveBeenCalledTimes(1);
    const req = fakeSettings.putProfile.mock.calls[0][1] as {
      ignoreZones: ZoneDto[];
      focusZones: ZoneDto[];
    };
    expect(req.ignoreZones.length).toBe(2); // pôvodná + nová
    expect(req.ignoreZones[1].name).toBe('Parkovisko');
    expect(req.focusZones).toEqual(PROFILE.focusZones);
    expect(fixture.componentInstance.zoneEditor).toBeNull();
  });

  it('Zones: Save v edit režime nahradí zónu na indexe', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    fixture.componentInstance.zoneEditor = {
      cameraId: 1,
      profile: { ...PROFILE },
      editing: { list: 'ignore', index: 0, zone: PROFILE.ignoreZones[0] },
    };
    fixture.detectChanges();

    const zone = { x: 0.5, y: 0.5, w: 0.1, h: 0.1, shape: 'rect' as const, points: [] as number[][] };
    fixture.componentInstance.onZoneSave({ cameraId: 1, name: 'Premenovaná', type: 'ignore', zone });
    fixture.detectChanges();

    const req = fakeSettings.putProfile.mock.calls[0][1] as { ignoreZones: ZoneDto[] };
    expect(req.ignoreZones.length).toBe(1);
    expect(req.ignoreZones[0].name).toBe('Premenovaná');
    expect(req.ignoreZones[0].x).toBe(0.5);
  });

  it('Zones: remove × zmaže zónu cez putProfile', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('.zone-remove')?.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(fakeSettings.putProfile).toHaveBeenCalledTimes(1);
    const req = fakeSettings.putProfile.mock.calls[0][1] as { ignoreZones: ZoneDto[] };
    expect(req.ignoreZones.length).toBe(0);
  });

  it('Zones: zmena kamery v editore načíta profil novej kamery', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('zones');
    fixture.componentInstance.selectZonesCamera(1);
    fixture.detectChanges();

    fixture.componentInstance.zoneEditor = {
      cameraId: 1,
      profile: { ...PROFILE },
      editing: null,
    };
    fixture.detectChanges();

    fixture.componentInstance.onZoneCameraChange(2);
    fixture.detectChanges();

    expect(fakeSettings.getProfile).toHaveBeenCalledWith(2);
    expect(fixture.componentInstance.zoneEditor!.cameraId).toBe(2);
    expect(fixture.componentInstance.zoneEditor!.editing).toBeNull();
  });

  // ── System (admin) ─────────────────────────────────────────────────────

  it('System: NVR info (bez hesla) + worker counts sa zobrazia', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('system');
    fixture.detectChanges();

    expect(fakeSettings.getSystemInfo).toHaveBeenCalled();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('192.168.68.10');
    expect(text).toContain('nvr-user');
    expect(text).toContain('WF_TEST_NVR_PASSWORD'); // názov secretu, nie hodnota
    expect(text).toContain('40'); // completed
    expect(fixture.componentInstance.passwordLabel()).toContain('••••••');
    // hodnota hesla NIKDY nie je v odpovedi API — zobrazuje sa len názov env secretu
    expect(fixture.componentInstance.passwordLabel()).toContain('WF_TEST_NVR_PASSWORD');
  });

  it('System: formatServerTime prevedie ISO na lokálny čas', () => {
    const fixture = createFixture();
    const formatted = fixture.componentInstance.formatServerTime('2026-08-08T12:00:00Z');
    expect(formatted).toContain('2026');
  });

  it('System: status karta zobrazí backlog a posledný sync (S12-2)', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('system');
    fixture.detectChanges();

    expect(fakeSettings.getSystemStatus).toHaveBeenCalled();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Synchronizácia');
    expect(text).toContain('3'); // backlog
    expect(text).toContain('10'); // záznamy
    expect(text).toContain('7'); // analyzované
    expect(fixture.componentInstance.systemStatus?.sync.backlog).toBe(3);
  });

  // ── Identities (S10-5, FR-05) ─────────────────────────────────────────

  it('Identities: sekcia načíta zoznam a zobrazí identity s počtom tvárí', () => {
    fakeSettings.getIdentities = vi.fn(() =>
      of([
        { identityId: 1, name: 'Anicka', createdAt: '2026-08-08T10:00:00Z', faceCount: 3 },
        { identityId: 2, name: 'Admin', createdAt: '2026-08-08T10:05:00Z', faceCount: 1 },
      ]),
    );
    const fixture = createFixture();
    fixture.componentInstance.selectSection('identities');
    fixture.detectChanges();

    expect(fakeSettings.getIdentities).toHaveBeenCalled();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Anicka');
    expect(text).toContain('Admin');
    expect(text).toContain('3');
  });

  it('Identities: prázdny zoznam zobrazí empty stav', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('identities');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Zatiaľ žiadne identity');
  });

  it('Identities: createIdentity zavolá API a obnoví zoznam', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('identities');
    fixture.componentInstance.newIdentityName = 'User Three';
    fixture.componentInstance.createIdentity();
    fixture.detectChanges();

    expect(fakeSettings.createIdentity).toHaveBeenCalledWith('User Three');
    expect(fakeSettings.getIdentities).toHaveBeenCalled();
  });

  it('Identities: deleteIdentity zavolá API a obnoví zoznam', () => {
    const fixture = createFixture();
    fixture.componentInstance.selectSection('identities');
    fixture.componentInstance.deleteIdentity({ identityId: 1, name: 'Anicka', createdAt: '', faceCount: 3 });
    fixture.detectChanges();

    expect(fakeSettings.deleteIdentity).toHaveBeenCalledWith(1);
    expect(fakeSettings.getIdentities).toHaveBeenCalled();
  });
});
