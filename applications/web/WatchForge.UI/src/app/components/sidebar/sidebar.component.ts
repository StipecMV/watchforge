import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CameraDto } from '../../models/video.model';
import { TranslatePipe } from '../../i18n/translate.pipe';
import { ThemeService, THEME_IDS, ThemeId } from '../../services/theme.service';

/**
 * Sidebar pre event-first dashboard (S6-3): zoznam kamier + prepínač tém (S6-8).
 * Legacy video zoznam (S6-1 scaffold, /api/videos) bol odstránený —
 * kamery sa načítavajú z GET /api/v1/cameras a výber kamery riadi
 * dashboard (detekcie dňa). Detailný Cameras panel príde v S6-4.
 */
@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css'],
})
export class SidebarComponent {
  @Input() cameras: CameraDto[] = [];
  @Input() selectedCameraId: number | null = null;
  /** Je prihlásený admin? (admin dostane navyše „Prihlásenie/Odhlásenie" a settings). */
  @Input() isAdmin = false;
  /** S22j: analýzy zapnuté/vypnuté (skryje Analýzy button). */
  @Input() analysesEnabled = true;
  @Output() cameraSelected = new EventEmitter<CameraDto>();
  /** S19: klik na „Live" — live view (1/8). */
  @Output() liveClick = new EventEmitter<void>();
  /** S22g: klik na „Analýzy" — dashboard/analýzy (pod Live view, konzistentne). */
  @Output() analysesClick = new EventEmitter<void>();
  /** Admin sa chce prihlásiť (login screen). */
  @Output() adminLoginClick = new EventEmitter<void>();
  /** Admin sa odhlásil. */
  @Output() logoutClick = new EventEmitter<void>();
  /** Admin: klik na ⚙ Nastavenia → Settings obrazovka. */
  @Output() settingsClick = new EventEmitter<void>();

  collapsed = false;

  /** S6-8: dostupné témy + aktívna. */
  readonly themeIds = THEME_IDS;
  readonly theme = this.themes.theme;

  constructor(private readonly themes: ThemeService) {}

  toggleSidebar() {
    this.collapsed = !this.collapsed;
  }

  /** Kamery zobrazené v sidebare — len AKTÍVNE (neaktívna CH9 sa nezobrazuje). */
  get visibleCameras(): CameraDto[] {
    return this.cameras.filter(c => c.isActive);
  }

  selectCamera(camera: CameraDto) {
    this.cameraSelected.emit(camera);
  }

  isActive(camera: CameraDto): boolean {
    return camera.cameraId === this.selectedCameraId;
  }

  /** S6-8: prepnutie témy (Light+Green / Dark+Purple / Contrast+Orange). */
  setTheme(theme: ThemeId) {
    this.themes.setTheme(theme);
  }
}
