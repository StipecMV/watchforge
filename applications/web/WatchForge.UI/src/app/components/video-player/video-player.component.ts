import {
  Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy,
  Output, SimpleChanges, ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DetectionRegion } from '../../models/video.model';
import { TranslatePipe } from '../../i18n/translate.pipe';

/**
 * Video player pre event-first dashboard (S6-3).
 *
 * Prehráva klip z GET /api/v1/clips/{id} (MP4, range requests) cez HTML5
 * <video>; nad videom kreslí detekčný bounding box (región 0..1).
 * Legacy konverzný polling (S6-1 scaffold) bol odstránený — klip už
 * vyrobil service (ClipExtractJobHandler) a API ho streamuje priamo.
 */
@Component({
  selector: 'app-video-player',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './video-player.component.html',
  styleUrls: ['./video-player.component.css'],
})
export class VideoPlayerComponent implements OnChanges, OnDestroy {
  @ViewChild('videoEl') videoEl!: ElementRef<HTMLVideoElement>;
  @ViewChild('overlayCanvas') overlayCanvas!: ElementRef<HTMLCanvasElement>;

  /** Streamovacia URL klipu (null = žiadny klip). */
  @Input() clipUrl: string | null = null;
  /** Detekčný región (0..1) vybranej udalosti pre overlay box. */
  @Input() region: DetectionRegion | null = null;
  /** Textová značka udalosti v HUD (napr. "3 · 14:02:11"). */
  @Input() eventLabel = '';

  /** Aktuálny čas prehrávania (sekundy) — dashboard seek/playhead. */
  @Output() timeUpdate = new EventEmitter<number>();
  @Output() durationChange = new EventEmitter<number>();
  @Output() playState = new EventEmitter<boolean>();

  currentTime = 0;
  duration = 0;
  isPlaying = false;

  private animFrameId = 0;

  ngOnChanges(changes: SimpleChanges) {
    if (changes['clipUrl']) {
      this.resetForNewClip();
    }
    if (changes['region'] || changes['clipUrl']) {
      this.drawOverlay();
    }
  }

  ngOnDestroy() {
    if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
  }

  private resetForNewClip() {
    this.currentTime = 0;
    this.duration = 0;
    this.isPlaying = false;
  }

  // ---- video events ----

  onMetadata() {
    const vid = this.videoEl?.nativeElement;
    if (!vid) return;
    this.duration = vid.duration || 0;
    this.durationChange.emit(this.duration);
    this.resizeOverlayCanvas();
    this.drawOverlay();
  }

  onTimeUpdate() {
    const vid = this.videoEl?.nativeElement;
    if (!vid) return;
    this.currentTime = vid.currentTime;
    // Robustnosť: duration môže byť známe až po prvom timeupdate
    // (napr. ak loadedmetadata neprišiel — jsdom testy, edge prehliadače).
    if (vid.duration && vid.duration !== this.duration) {
      this.duration = vid.duration;
      this.durationChange.emit(this.duration);
    }
    this.timeUpdate.emit(this.currentTime);
    this.drawOverlay();
  }

  onPlay() {
    this.isPlaying = true;
    this.playState.emit(true);
  }

  onPause() {
    this.isPlaying = false;
    this.playState.emit(false);
  }

  onEnded() {
    this.isPlaying = false;
    this.playState.emit(false);
  }

  // ---- transport ----

  togglePlay() {
    const vid = this.videoEl?.nativeElement;
    if (!vid) return;
    if (this.isPlaying) {
      vid.pause();
    } else {
      void vid.play();
    }
  }

  seekTo(time: number) {
    const vid = this.videoEl?.nativeElement;
    if (!vid || !isFinite(time)) return;
    vid.currentTime = Math.max(0, Math.min(this.duration || time, time));
    this.onTimeUpdate();
  }

  formatTime(secs: number): string {
    if (!isFinite(secs)) return '0:00';
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    const s = Math.floor(secs % 60);
    if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    return `${m}:${String(s).padStart(2, '0')}`;
  }

  // ---- overlay (detekčný box) ----

  /** Reálny vykreslený obdĺžnik videa (object-fit: contain). */
  private videoRenderRect(vid: HTMLVideoElement): { w: number; h: number } {
    const cW = vid.clientWidth;
    const cH = vid.clientHeight;
    const vW = vid.videoWidth || cW;
    const vH = vid.videoHeight || cH;
    if (!vW || !vH) return { w: cW, h: cH };
    const scale = Math.min(cW / vW, cH / vH);
    return { w: Math.round(vW * scale), h: Math.round(vH * scale) };
  }

  private resizeOverlayCanvas() {
    const canvas = this.overlayCanvas?.nativeElement;
    const vid = this.videoEl?.nativeElement;
    if (!canvas || !vid) return;
    const { w, h } = this.videoRenderRect(vid);
    canvas.width = w || 640;
    canvas.height = h || 360;
  }

  private drawOverlay() {
    const canvas = this.overlayCanvas?.nativeElement;
    const vid = this.videoEl?.nativeElement;
    if (!canvas || !vid) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const { w, h } = this.videoRenderRect(vid);
    if (w !== canvas.width || h !== canvas.height) {
      canvas.width = w;
      canvas.height = h;
    }
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    const region = this.region;
    if (!region || !this.clipUrl) return;

    ctx.strokeStyle = 'rgba(255, 60, 60, 0.95)';
    ctx.lineWidth = 2;
    ctx.strokeRect(
      region.x * canvas.width,
      region.y * canvas.height,
      region.width * canvas.width,
      region.height * canvas.height,
    );
  }
}
