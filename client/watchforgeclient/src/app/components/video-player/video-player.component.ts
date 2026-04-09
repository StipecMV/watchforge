import {
  Component, ElementRef, Input, OnChanges, OnDestroy,
  SimpleChanges, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { interval, Subscription } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { ConversionStatus, DetectionEvent, DetectionsFile, VideoItem } from '../../models/video.model';
import { TimelineComponent } from '../timeline/timeline.component';

@Component({
  selector: 'app-video-player',
  standalone: true,
  imports: [CommonModule, TimelineComponent],
  templateUrl: './video-player.component.html',
  styleUrls: ['./video-player.component.css']
})
export class VideoPlayerComponent implements OnChanges, OnDestroy {
  @ViewChild('videoEl') videoEl!: ElementRef<HTMLVideoElement>;
  @ViewChild('overlayCanvas') overlayCanvas!: ElementRef<HTMLCanvasElement>;

  @Input() video: VideoItem | null = null;

  conversionStatus: ConversionStatus | null = null;
  detections: DetectionsFile | null = null;
  currentTime = 0;
  duration = 0;
  isPlaying = false;
  loading = false;

  private pollSub?: Subscription;
  private animFrameId = 0;

  constructor(private api: ApiService) {}

  ngOnChanges(changes: SimpleChanges) {
    if (changes['video'] && this.video) {
      this.loadVideo();
    }
  }

  ngOnDestroy() {
    this.stopPolling();
    if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
  }

  private loadVideo() {
    this.stopPolling();
    this.conversionStatus = null;
    this.detections = null;
    this.currentTime = 0;
    this.duration = 0;
    this.isPlaying = false;
    this.loading = true;

    // Load detections if available
    if (this.video!.hasDetections) {
      this.api.getDetections(this.video!.id).subscribe({
        next: det => { this.detections = det; },
        error: () => { this.detections = null; }
      });
    }

    // Check conversion status first
    this.api.getConversionStatus(this.video!.id).subscribe({
      next: status => {
        this.conversionStatus = status;
        this.loading = false;
        if (status.status === 'ready') {
          this.setVideoSrc();
        } else if (status.status !== 'error') {
          // Trigger conversion by hitting stream endpoint, then poll
          this.triggerConversion();
        }
      },
      error: () => {
        this.loading = false;
        this.setVideoSrc(); // try anyway
      }
    });
  }

  private triggerConversion() {
    // Hit stream endpoint to trigger conversion (it returns 202 and starts)
    fetch(this.api.streamUrl(this.video!.id)).catch(() => {});
    this.startPolling();
  }

  private startPolling() {
    this.pollSub = interval(2000).subscribe(() => {
      if (!this.video) return;
      this.api.getConversionStatus(this.video.id).subscribe(status => {
        this.conversionStatus = status;
        if (status.status === 'ready') {
          this.stopPolling();
          this.setVideoSrc();
        } else if (status.status === 'error') {
          this.stopPolling();
        }
      });
    });
  }

  private stopPolling() {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }

  private setVideoSrc() {
    const vid = this.videoEl?.nativeElement;
    if (!vid || !this.video) return;
    vid.src = this.api.streamUrl(this.video.id);
    vid.load();
  }

  onVideoMetadata() {
    const vid = this.videoEl.nativeElement;
    this.duration = vid.duration;
    this.resizeOverlayCanvas();
  }

  onTimeUpdate() {
    const vid = this.videoEl.nativeElement;
    this.currentTime = vid.currentTime;
    this.drawOverlay();
  }

  onPlay() { this.isPlaying = true; }
  onPause() { this.isPlaying = false; }
  onEnded() { this.isPlaying = false; }

  togglePlay() {
    const vid = this.videoEl?.nativeElement;
    if (!vid) return;
    if (this.isPlaying) { vid.pause(); } else { vid.play(); }
  }

  seekTo(time: number) {
    const vid = this.videoEl?.nativeElement;
    if (vid) vid.currentTime = time;
  }

  /** Returns the actual rendered video rect (object-fit: contain) */
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
    if (!canvas) return;
    const ctx = canvas.getContext('2d')!;

    const vid = this.videoEl.nativeElement;
    const { w, h } = this.videoRenderRect(vid);
    canvas.width = w;
    canvas.height = h;
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    if (!this.detections?.events) return;

    const nowMs = this.currentTime * 1000;
    const activeEvents = this.detections.events.filter(
      ev => Math.abs(ev.timestampMs - nowMs) <= 250
    );

    ctx.strokeStyle = 'rgba(255, 50, 50, 0.9)';
    ctx.lineWidth = 2;
    for (const ev of activeEvents) {
      for (const r of ev.regions) {
        ctx.strokeRect(
          r.x * canvas.width,
          r.y * canvas.height,
          r.width * canvas.width,
          r.height * canvas.height
        );
      }
    }
  }

  get eventsForTimeline(): DetectionEvent[] {
    return this.detections?.events ?? [];
  }

  get conversionProgress(): number {
    return this.conversionStatus?.progress ?? 0;
  }

  get isConverting(): boolean {
    return this.conversionStatus?.status === 'converting' ||
           this.conversionStatus?.status === 'not_started';
  }

  get conversionError(): boolean {
    return this.conversionStatus?.status === 'error';
  }

  formatTime(secs: number): string {
    if (!isFinite(secs)) return '0:00';
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    const s = Math.floor(secs % 60);
    if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    return `${m}:${String(s).padStart(2, '0')}`;
  }
}
