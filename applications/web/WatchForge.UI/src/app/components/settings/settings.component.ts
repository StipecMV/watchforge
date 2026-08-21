import { Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { SettingsService } from '../../services/settings.service';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { CameraDto } from '../../models/video.model';
import {
  DetectionProfile,
  IdentityInfo,
  SettingsUser,
  SystemInfo,
  SystemStatus,
  ZoneDto,
} from '../../models/settings.model';
import { AvatarComponent } from '../icons/avatar.component';
import { AVATAR_SPECS } from '../icons/icons';
import { ZoneEditorComponent, ZoneSaveEvent, ZoneType } from '../zone-editor/zone-editor.component';

/** Sekcie Settings (design handoff settings-screen.jsx; zones → S6-6 editor). */
export type SettingsSection = 'profile' | 'cameras' | 'detection' | 'zones' | 'users' | 'security' | 'system' | 'identities';

/** Rozmery 1080p framu pre prevod minContourArea ↔ px (FR-03: analýza na 1080p). */
const FRAME_W = 1920;
const FRAME_H = 1080;

/** Pracovná úprava kamery (dirty tracking pred Save). */
interface CameraEdit {
  friendlyName: string;
  isActive: boolean;
}

/** Referencia na editovanú zónu v profile (list + index — backend nemá id zóny). */
interface ZoneEditRef {
  list: ZoneType;
  index: number;
  zone: ZoneDto;
}

/** Stav otvoreného zone editora (S6-6). */
interface ZoneEditorState {
  cameraId: number;
  profile: DetectionProfile;
  editing: ZoneEditRef | null;
}

/**
 * Settings obrazovka (S6-5, FR-11/FR-12, design handoff settings-screen.jsx).
 *
 * Sekcie: Profile (avatar 10 glyfov, locale), Cameras (friendly name, 40 ikon,
 * active — Save/Cancel), Detection (citlivosť, min veľkosť, prah — profily API),
 * Users (admin: zoznam + reset hesla), Security (zmena hesla + aktuálna session),
 * System (admin: NVR pripojenie bez hesla, worker status, API info).
 * Focus zones editor → S6-6.
 *
 * Zásady: žiadne fiktívne dáta — všetko sa číta/zapisuje cez API; NVR heslo sa
 * nikdy nezobrazuje (passwordSecretEnv = názov env secretu, NFR-05).
 */
@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, AvatarComponent, ZoneEditorComponent],
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.css'],
})
export class SettingsComponent implements OnInit {
  /** Všetky kamery (z app shellu) — Cameras + Detection sekcia. */
  @Input() cameras: CameraDto[] = [];
  /** Len AKTÍVNE kamery — pre výbery (Detekcia/Zóny); neaktívna CH9 sa nezobrazuje. */
  get activeCameras(): CameraDto[] {
    return this.cameras.filter(c => c.isActive);
  }

  readonly auth = inject(AuthService);
  private readonly settings = inject(SettingsService);
  readonly i18n = inject(I18nService);

  readonly Math = Math;
  readonly String = String;
  readonly avatarSpecs = AVATAR_SPECS;

  activeSection: SettingsSection = 'profile';
  isAdmin = false;

  // ── Profile ────────────────────────────────────────────────────────────
  avatarId = 1;
  locale: 'sk' | 'en' = 'sk';
  profileError: string | null = null;

  // ── Cameras ────────────────────────────────────────────────────────────
  edits = new Map<number, CameraEdit>();
  camerasDirty = false;
  camerasError: string | null = null;

  // ── Detection ──────────────────────────────────────────────────────────
  detectionCameraId: number | null = null;
  profile: DetectionProfile | null = null;
  /** UI hodnoty: citlivosť 0..100, min veľkosť px (8..256), prah 0..100. */
  uiSensitivity = 50;
  uiMinSizePx = 48;
  uiIntensityThreshold = 3;
  profileLoading = false;
  profileLoadError = false;
  profileSaveError: string | null = null;
  profileSaved = false;

  // ── Users (admin) ──────────────────────────────────────────────────────
  users: SettingsUser[] = [];
  usersError = false;
  /** username čakajúci na potvrdenie resetu (dvojklik pattern). */
  resetConfirmFor: string | null = null;
  resetMessage: string | null = null;

  // ── Zones (S6-6: focus zone editor) ───────────────────────────────────
  zonesCameraId: number | null = null;
  zonesProfile: DetectionProfile | null = null;
  zonesLoading = false;
  zonesError = false;
  zonesSaved = false;
  /** Otvorený editor (namiesto zoznamu) — null = zoznam zón. */
  zoneEditor: ZoneEditorState | null = null;

  // ── Security ───────────────────────────────────────────────────────────
  secCurrent = '';
  secNew = '';
  secConfirm = '';
  secMessage: { kind: 'ok' | 'error'; text: string } | null = null;
  /** Aktuálna session — backend nemá session store, ukazujeme len túto. */
  readonly sessionDevice = typeof navigator !== 'undefined' ? navigator.userAgent : '';

  // ── System (admin) ─────────────────────────────────────────────────────
  systemInfo: SystemInfo | null = null;
  systemError = false;
  /** S12-2: operatívny status (sync/backlog) z /system/status. */
  systemStatus: SystemStatus | null = null;
  systemStatusError = false;

  ngOnInit() {
    this.isAdmin = this.auth.currentUser()?.role === 'admin';
    const me = this.auth.currentUser();
    if (me) {
      this.avatarId = me.avatarId;
      this.locale = me.locale === 'en' ? 'en' : 'sk';
    }
    this.loadCamerasSection();
    if (this.isAdmin) {
      this.loadUsers();
      this.loadSystemInfo();
      this.loadSystemStatus();
    }
  }

  /** S12-2: načítanie operatívneho statusu (sync/backlog). */
  private loadSystemStatus() {
    this.systemStatusError = false;
    this.settings.getSystemStatus().subscribe({
      next: status => (this.systemStatus = status),
      error: () => (this.systemStatusError = true),
    });
  }

  // ── navigácia sekcií ───────────────────────────────────────────────────

  selectSection(section: SettingsSection) {
    this.activeSection = section;
    if (section === 'detection') this.loadDetectionProfile();
    if (section === 'identities') this.loadIdentities();
    if (section === 'zones' && this.zonesCameraId === null) {
      const first = this.cameras.find(c => c.isActive) ?? this.cameras[0];
      if (first) this.selectZonesCamera(first.cameraId);
    }
  }

  // ── Profile ────────────────────────────────────────────────────────────

  /** Auto-save avatara (design: picker bez Save tlačidla). */
  pickAvatar(id: string) {
    const num = Number(id.replace('av', ''));
    this.avatarId = num;
    this.saveProfile();
  }

  /** Auto-save locale prepínača (SK/EN, NFR-08). */
  setLocale(locale: 'sk' | 'en') {
    this.locale = locale;
    this.i18n.setLocale(locale);
    this.saveProfile();
  }

  private saveProfile() {
    this.profileError = null;
    this.settings.updateMe(this.avatarId, this.locale).subscribe({
      next: updated => {
        this.auth.currentUser.update(u => (u ? { ...u, avatarId: updated.avatarId, locale: updated.locale } : u));
      },
      error: () => {
        this.profileError = 'settings.profile.saveError';
      },
    });
  }

  // ── Cameras ────────────────────────────────────────────────────────────

  private loadCamerasSection() {
    this.edits = new Map(this.cameras.map(c => [c.cameraId, this.toEdit(c)]));
    this.camerasDirty = false;
  }

  private toEdit(c: CameraDto): CameraEdit {
    return { friendlyName: c.friendlyName, isActive: c.isActive };
  }

  edit(cameraId: number): CameraEdit {
    const e = this.edits.get(cameraId);
    if (!e) {
      const cam = this.cameras.find(c => c.cameraId === cameraId);
      const created = cam ? this.toEdit(cam) : { friendlyName: '', isActive: true };
      this.edits.set(cameraId, created);
      return created;
    }
    return e;
  }

  markDirty(_cameraId: number) {
    this.camerasDirty = true;
    this.camerasError = null;
  }

  cameraHasChanges(c: CameraDto): boolean {
    const e = this.edits.get(c.cameraId);
    if (!e) return false;
    return e.friendlyName !== c.friendlyName || e.isActive !== c.isActive;
  }

  cancelCameras() {
    this.loadCamerasSection();
  }

  /** Uloží zmenené kamery (len tie s rozdielom) — PUT /cameras/{id}. */
  saveCameras() {
    this.camerasError = null;
    const changed = this.cameras.filter(c => this.cameraHasChanges(c));
    if (changed.length === 0) {
      this.camerasDirty = false;
      return;
    }
    let done = 0;
    for (const cam of changed) {
      const e = this.edit(cam.cameraId);
      this.settings.updateCamera(cam.cameraId, e.friendlyName.trim(), cam.iconId || 'camera', e.isActive).subscribe({
        next: updated => {
          this.cameras = this.cameras.map(c =>
            c.cameraId === updated.cameraId
              ? { ...c, friendlyName: updated.friendlyName, iconId: updated.iconId, isActive: updated.isActive }
              : c,
          );
          if (++done === changed.length) this.camerasDirty = false;
        },
        error: () => {
          this.camerasError = 'settings.cameras.saveError';
        },
      });
    }
  }

  // ── Detection ──────────────────────────────────────────────────────────

  /** Switch: false = JEDEN zdieľaný profil pre všetky kamery (default), true = per-kamera. */
  detectionPerCamera = false;

  /** Prepnutie režimu — načíta príslušný profil. */
  toggleDetectionMode() {
    this.detectionPerCamera = !this.detectionPerCamera;
    if (this.detectionPerCamera) {
      // per-kamera: predvybrať prvú kameru, ak ešte nie je vybraná
      if (this.detectionCameraId === null && this.activeCameras.length > 0) {
        this.detectionCameraId = this.activeCameras[0].cameraId;
      }
    }
    this.loadDetectionProfile();
  }

  /** Načíta profil podľa režimu: shared (všetky kamery) alebo per-kamera. */
  loadDetectionProfile() {
    this.profileLoading = true;
    this.profileLoadError = false;
    this.profileSaved = false;
    const load$ = this.detectionPerCamera
      ? (this.detectionCameraId !== null ? this.settings.getProfile(this.detectionCameraId) : null)
      : this.settings.getSharedProfile();
    if (load$ === null) {
      this.profileLoading = false;
      return;
    }
    load$.subscribe({
      next: p => {
        this.profile = p;
        this.uiSensitivity = Math.round(p.sensitivity * 100);
        this.uiMinSizePx = this.minContourToPx(p.minContourArea);
        this.uiIntensityThreshold = Math.round(p.intensityThreshold * 100);
        this.profileLoading = false;
      },
      error: () => {
        // 404 = žiadny profil ešte neexistuje — použijú sa defaulty, Save vytvorí
        this.profile = null;
        this.uiSensitivity = 50;
        this.uiMinSizePx = 48;
        this.uiIntensityThreshold = 3;
        this.profileLoading = false;
        this.profileLoadError = true;
      },
    });
  }

  selectDetectionCamera(cameraId: number) {
    this.detectionCameraId = cameraId;
    this.loadDetectionProfile();
  }

  /** minContourArea (0..1, plocha 1080p) → veľkosť strany v px (8..256). */
  minContourToPx(area: number): number {
    if (area <= 0) return 8;
    const px = Math.sqrt(area * FRAME_W * FRAME_H);
    return Math.round(Math.min(256, Math.max(8, px)));
  }

  /** veľkosť strany v px → minContourArea (normalizovaná plocha). */
  pxToMinContour(px: number): number {
    const clamped = Math.min(256, Math.max(8, px));
    return (clamped * clamped) / (FRAME_W * FRAME_H);
  }

  /** Uloží novú verziu profilu (zachová existujúce zóny — editor je S6-6). */
  saveDetection() {
    this.profileSaveError = null;
    this.profileSaved = false;
    const request = {
      sensitivity: this.uiSensitivity / 100,
      intensityThreshold: this.uiIntensityThreshold / 100,
      minContourArea: this.pxToMinContour(this.uiMinSizePx),
      ignoreZones: this.profile?.ignoreZones ?? [],
      focusZones: this.profile?.focusZones ?? [],
    };
    const save$ = this.detectionPerCamera
      ? (this.detectionCameraId !== null ? this.settings.putProfile(this.detectionCameraId, request) : null)
      : this.settings.putSharedProfile(request);
    if (save$ === null) return;
    save$.subscribe({
      next: p => {
        this.profile = p;
        this.profileSaved = true;
      },
      error: () => {
        this.profileSaveError = 'settings.detection.saveError';
      },
    });
  }

  // ── Zones (S6-6: focus zone editor) ───────────────────────────────────

  /** Výber kamery v zozname zón → načíta jej aktívny profil. */
  selectZonesCamera(cameraId: number) {
    this.zonesCameraId = cameraId;
    this.loadZonesProfile(cameraId);
  }

  private loadZonesProfile(cameraId: number) {
    this.zonesLoading = true;
    this.zonesError = false;
    this.zonesSaved = false;
    this.settings.getProfile(cameraId).subscribe({
      next: p => {
        this.zonesProfile = p;
        this.zonesLoading = false;
        // Otvorený editor sleduje profil aktívnej kamery (zmena kamery v editore).
        if (this.zoneEditor && this.zoneEditor.cameraId === cameraId) {
          this.zoneEditor = { ...this.zoneEditor, profile: p };
        }
      },
      error: () => {
        // 404 = žiadny profil — zoznam prázdny, Save/Add vytvorí profil
        this.zonesProfile = null;
        this.zonesLoading = false;
        this.zonesError = true;
      },
    });
  }

  /** Zoznam zón (ignore + focus) pre template — s typom a indexom v liste. */
  zoneList(): { zone: ZoneDto; type: ZoneType; index: number }[] {
    const out: { zone: ZoneDto; type: ZoneType; index: number }[] = [];
    (this.zonesProfile?.ignoreZones ?? []).forEach((z, i) => out.push({ zone: z, type: 'ignore', index: i }));
    (this.zonesProfile?.focusZones ?? []).forEach((z, i) => out.push({ zone: z, type: 'focus', index: i }));
    return out;
  }

  /** Plocha zóny v % framu (1 des. miesto) — pre zoznam. */
  zoneAreaPct(zone: ZoneDto): string {
    return (zone.w * zone.h * 100).toFixed(1);
  }

  /** Otvorí editor: null = nová zóna, inak editácia existujúcej. */
  openZoneEditor(editing: ZoneEditRef | null = null) {
    if (this.zonesCameraId === null) return;
    // Profil ešte neexistuje (404) → otvorí sa editor s prázdnym profilom;
    // Save vytvorí nový profil cez PUT.
    const profile: DetectionProfile = this.zonesProfile ?? {
      configVersionId: 0,
      cameraId: this.zonesCameraId,
      profileType: 'per_camera',
      sensitivity: 0.3,
      intensityThreshold: 0.03,
      minContourArea: 0.0011,
      ignoreZones: [],
      focusZones: [],
    };
    this.zoneEditor = { cameraId: this.zonesCameraId, profile: { ...profile }, editing };
  }

  onZoneCancel() {
    this.zoneEditor = null;
  }

  /** Zmena kamery priamo v editore → načíta profil novej kamery, zruší edit ref. */
  onZoneCameraChange(cameraId: number) {
    if (!this.zoneEditor) return;
    this.zoneEditor = { ...this.zoneEditor, cameraId, editing: null };
    this.zonesCameraId = cameraId;
    this.loadZonesProfile(cameraId);
  }

  /** Save z editora: pridá/nahradí zónu v profile a uloží novú verziu (PUT). */
  onZoneSave(event: ZoneSaveEvent) {
    const ed = this.zoneEditor;
    if (!ed) return;
    const zone: ZoneDto = {
      x: event.zone.x,
      y: event.zone.y,
      w: event.zone.w,
      h: event.zone.h,
      name: event.name,
      shape: event.zone.shape ?? 'rect',
      points: event.zone.points?.length ? event.zone.points.map(p => [p[0], p[1]]) : undefined,
    };
    const ignoreZones = [...ed.profile.ignoreZones];
    const focusZones = [...ed.profile.focusZones];
    if (ed.editing) {
      const src = ed.editing.list === 'ignore' ? ignoreZones : focusZones;
      if (ed.editing.list === event.type) {
        src[ed.editing.index] = zone;
      } else {
        // Zmena typu: odstrániť z pôvodného listu, pridať do nového
        src.splice(ed.editing.index, 1);
        (event.type === 'ignore' ? ignoreZones : focusZones).push(zone);
      }
    } else {
      (event.type === 'ignore' ? ignoreZones : focusZones).push(zone);
    }
    this.saveZones(event.cameraId, ed.profile, ignoreZones, focusZones);
  }

  /** Odstránenie zóny (remove × v zozname). */
  removeZone(list: ZoneType, index: number) {
    if (this.zonesCameraId === null || !this.zonesProfile) return;
    const ignoreZones = [...this.zonesProfile.ignoreZones];
    const focusZones = [...this.zonesProfile.focusZones];
    (list === 'ignore' ? ignoreZones : focusZones).splice(index, 1);
    this.saveZones(this.zonesCameraId, this.zonesProfile, ignoreZones, focusZones);
  }

  /** PUT profilu so zachovaním detekčných hodnôt — zmena platí pre nové analýzy (FR-06). */
  private saveZones(
    cameraId: number,
    profile: DetectionProfile,
    ignoreZones: ZoneDto[],
    focusZones: ZoneDto[],
  ) {
    this.zonesError = false;
    this.zonesSaved = false;
    this.settings.putProfile(cameraId, {
      sensitivity: profile.sensitivity,
      intensityThreshold: profile.intensityThreshold,
      minContourArea: profile.minContourArea,
      ignoreZones,
      focusZones,
    }).subscribe({
      next: p => {
        this.zonesProfile = p;
        this.zonesSaved = true;
        this.zoneEditor = null;
      },
      error: () => {
        this.zonesError = true;
      },
    });
  }

  // ── Users (admin) ──────────────────────────────────────────────────────

  private loadUsers() {
    this.usersError = false;
    this.settings.getUsers().subscribe({
      next: users => (this.users = users),
      error: () => (this.usersError = true),
    });
  }

  /** Dvojklik pattern: prvý klik armuje, druhý vykoná reset. */
  requestReset(username: string) {
    if (this.resetConfirmFor !== username) {
      this.resetConfirmFor = username;
      this.resetMessage = null;
      return;
    }
    this.resetConfirmFor = null;
    this.settings.resetPassword(username).subscribe({
      next: () => {
        this.resetMessage = 'settings.users.resetDone';
        this.loadUsers();
      },
      error: () => {
        this.resetMessage = 'settings.users.resetError';
      },
    });
  }

  isMe(user: SettingsUser): boolean {
    return user.username.toLowerCase() === (this.auth.currentUser()?.username ?? '').toLowerCase();
  }

  // ── Security ───────────────────────────────────────────────────────────

  changePassword() {
    this.secMessage = null;
    if (this.secNew.length < 8) {
      this.secMessage = { kind: 'error', text: 'settings.security.tooShort' };
      return;
    }
    if (this.secNew !== this.secConfirm) {
      this.secMessage = { kind: 'error', text: 'settings.security.mismatch' };
      return;
    }
    this.settings.changePassword(this.secCurrent, this.secNew).subscribe({
      next: () => {
        this.secCurrent = '';
        this.secNew = '';
        this.secConfirm = '';
        this.secMessage = { kind: 'ok', text: 'settings.security.updated' };
      },
      error: err => {
        this.secMessage = {
          kind: 'error',
          text: err?.status === 400 ? 'settings.security.wrongCurrent' : 'settings.security.updateError',
        };
      },
    });
  }

  // ── System (admin) ─────────────────────────────────────────────────────

  private loadSystemInfo() {
    this.systemError = false;
    this.settings.getSystemInfo().subscribe({
      next: info => (this.systemInfo = info),
      error: () => (this.systemError = true),
    });
  }

  formatServerTime(iso: string): string {
    return new Date(iso).toLocaleString('sk-SK', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }

  /** Názov env secretu skrátený na bezpečné zobrazenie (nikdy hodnota hesla). */
  passwordLabel(): string {
    const env = this.systemInfo?.nvr.passwordSecretEnv ?? '';
    return env ? `•••••• (${env})` : '••••••';
  }

  // ── Identities (S10-5, FR-05) ─────────────────────────────────────────

  identities: IdentityInfo[] = [];
  identitiesError = false;
  newIdentityName = '';

  private loadIdentities() {
    this.identitiesError = false;
    this.settings.getIdentities().subscribe({
      next: list => (this.identities = list),
      error: () => (this.identitiesError = true),
    });
  }

  createIdentity() {
    const name = this.newIdentityName.trim();
    if (!name) return;
    this.settings.createIdentity(name).subscribe({
      next: () => {
        this.newIdentityName = '';
        this.loadIdentities();
      },
      error: () => (this.identitiesError = true),
    });
  }

  deleteIdentity(identity: IdentityInfo) {
    this.settings.deleteIdentity(identity.identityId).subscribe({
      next: () => this.loadIdentities(),
      error: () => (this.identitiesError = true),
    });
  }
}
