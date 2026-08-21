import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { SidebarComponent } from './sidebar.component';
import { I18nService } from '../../i18n/i18n.service';
import { ThemeService } from '../../services/theme.service';
import { CameraDto } from '../../models/video.model';

const CAMERAS: CameraDto[] = [
  { cameraId: 1, nvrId: 1, channel: 0, friendlyName: 'Dvor', iconId: 'garden', isActive: true },
  { cameraId: 2, nvrId: 1, channel: 1, friendlyName: 'Garáž', iconId: 'parking', isActive: true },
];

describe('SidebarComponent (S6-3, camera selection)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  it('zobrazí prázdny stav po slovensky (default locale)', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Žiadne kamery');
  });

  it('zobrazí prázdny stav po anglicky po prepnutí jazyka', () => {
    TestBed.inject(I18nService).setLocale('en');
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('No cameras found');
  });

  it('vyrenderuje zoznam kamier s friendly name a kanálom', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', CAMERAS);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Dvor');
    expect(el.textContent).toContain('CH1');
    expect(el.textContent).toContain('Garáž');
    expect(el.textContent).toContain('CH2');
  });

  it('neaktívna kamera sa v zozname NEzobrazuje (filter isActive)', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', [
      { cameraId: 1, nvrId: 1, channel: 0, friendlyName: 'Dvor', iconId: 'garden', isActive: true },
      { cameraId: 9, nvrId: 1, channel: 8, friendlyName: 'Kamera 9', iconId: 'camera', isActive: false },
    ]);
    fixture.detectChanges();
    const items = fixture.nativeElement.querySelectorAll('.camera-item');
    expect(items.length).toBe(1);
    expect(fixture.nativeElement.textContent).not.toContain('Kamera 9');
  });

  it('klik na kameru emituje cameraSelected', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', CAMERAS);
    fixture.detectChanges();
    const comp = fixture.componentInstance;
    let selected: CameraDto | null = null;
    comp.cameraSelected.subscribe((c: CameraDto) => (selected = c));

    const items = fixture.nativeElement.querySelectorAll('.camera-item');
    (items[1] as HTMLElement).click();
    expect(selected?.cameraId).toBe(2);
  });

  it('vybraná kamera má triedu active', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', CAMERAS);
    fixture.componentRef.setInput('selectedCameraId', 2);
    fixture.detectChanges();
    const items = fixture.nativeElement.querySelectorAll('.camera-item');
    expect(items[1].classList.contains('active')).toBe(true);
    expect(items[0].classList.contains('active')).toBe(false);
  });

  // ── S6-8: prepínač tém ────────────────────────────────────────────────

  it('zobrazí tri témy (Light+Green / Dark+Purple / Contrast+Orange)', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const dots = fixture.nativeElement.querySelectorAll('.theme-dot');
    expect(dots.length).toBe(3);
    expect((dots[0] as HTMLElement).dataset['themeId']).toBe('light-green');
    expect((dots[1] as HTMLElement).dataset['themeId']).toBe('dark-purple');
    expect((dots[2] as HTMLElement).dataset['themeId']).toBe('contrast-orange');
  });

  it('klik na tému prepne ThemeService a označí aktívnu guličku', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const service = TestBed.inject(ThemeService);
    service.setTheme('light-green');
    fixture.detectChanges();

    const dots = fixture.nativeElement.querySelectorAll('.theme-dot');
    (dots[2] as HTMLElement).click(); // contrast-orange
    fixture.detectChanges();

    expect(service.theme()).toBe('contrast-orange');
    expect(dots[2].classList.contains('active')).toBe(true);
    expect(document.documentElement.dataset['theme']).toBe('contrast-orange');
  });

  // ── S22c: admin login/logout/settings tlačidlá ────────────────────────

  it('neadmin vidí 🔐 Prihlásenie admina a NIE ⚙ Nastavenia', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', CAMERAS);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.admin-settings')).toBeNull();
    expect(el.textContent).toContain('Prihlásenie admina');
  });

  it('admin vidí ⚙ Nastavenia + Odhlásiť sa; klik emituje settingsClick', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.componentRef.setInput('cameras', CAMERAS);
    fixture.componentRef.setInput('isAdmin', true);
    fixture.detectChanges();
    const comp = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;

    let settingsEmitted = false;
    comp.settingsClick.subscribe(() => (settingsEmitted = true));

    const settingsBtn = el.querySelector('.admin-settings');
    expect(settingsBtn).not.toBeNull();
    (settingsBtn as HTMLElement).click();
    expect(settingsEmitted).toBe(true);
    expect(el.textContent).toContain('Odhlásiť sa');
  });
});
