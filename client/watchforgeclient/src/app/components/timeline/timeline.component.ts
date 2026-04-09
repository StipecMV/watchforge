import {
  AfterViewInit, Component, ElementRef, EventEmitter, HostListener,
  Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DetectionEvent } from '../../models/video.model';

@Component({
  selector: 'app-timeline',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './timeline.component.html',
  styleUrls: ['./timeline.component.css']
})
export class TimelineComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;
  private resizeObserver?: ResizeObserver;

  @Input() duration = 0;       // total video duration in seconds
  @Input() currentTime = 0;    // current playback time in seconds
  @Input() events: DetectionEvent[] = [];
  @Output() seek = new EventEmitter<number>(); // emits time in seconds

  private zoomStart = 0;    // visible window start (seconds)
  private zoomEnd = 0;      // visible window end (seconds)
  private zoomEndDefault = 0;
  private dragging = false;
  private animFrameId = 0;

  ngAfterViewInit() {
    this.resizeObserver = new ResizeObserver(() => this.draw());
    this.resizeObserver.observe(this.canvas);
    // Must be passive:false so we can preventDefault and stop page scroll
    this.canvas.addEventListener('wheel', this.handleWheel, { passive: false });
    this.draw();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['duration'] && this.duration > 0) {
      this.zoomStart = 0;
      this.zoomEnd = this.duration;
      this.zoomEndDefault = this.duration;
    }
    this.draw();
  }

  ngOnDestroy() {
    if (this.animFrameId) cancelAnimationFrame(this.animFrameId);
    this.resizeObserver?.disconnect();
    this.canvas.removeEventListener('wheel', this.handleWheel);
  }

  private get canvas(): HTMLCanvasElement {
    return this.canvasRef.nativeElement;
  }

  private get ctx(): CanvasRenderingContext2D {
    return this.canvas.getContext('2d')!;
  }

  private timeToX(time: number): number {
    const w = this.canvas.width;
    const window = this.zoomEnd - this.zoomStart;
    if (window <= 0) return 0;
    return ((time - this.zoomStart) / window) * w;
  }

  private xToTime(x: number): number {
    const w = this.canvas.width;
    const window = this.zoomEnd - this.zoomStart;
    return this.zoomStart + (x / w) * window;
  }

  draw() {
    const canvas = this.canvas;
    const ctx = this.ctx;
    const w = canvas.width = canvas.offsetWidth || 800;
    const h = canvas.height = canvas.offsetHeight || 48;

    if (this.duration <= 0) {
      ctx.fillStyle = '#1a1a1a';
      ctx.fillRect(0, 0, w, h);
      ctx.fillStyle = '#555';
      ctx.font = '12px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('No video loaded', w / 2, h / 2 + 4);
      return;
    }

    // Background
    ctx.fillStyle = '#1a1a1a';
    ctx.fillRect(0, 0, w, h);

    // Track background
    const trackY = Math.floor(h * 0.35);
    const trackH = Math.floor(h * 0.3);
    ctx.fillStyle = '#2a2a2a';
    ctx.fillRect(0, trackY, w, trackH);

    // Motion event segments (red)
    ctx.fillStyle = 'rgba(220, 50, 50, 0.85)';
    for (const ev of this.events) {
      const t = ev.timestampMs / 1000;
      if (t < this.zoomStart || t > this.zoomEnd) continue;
      const x = this.timeToX(t);
      const segW = Math.max(2, w / ((this.zoomEnd - this.zoomStart) * 10));
      ctx.fillRect(x - segW / 2, trackY, segW, trackH);
    }

    // Time tick marks
    const window = this.zoomEnd - this.zoomStart;
    const tickInterval = this.niceInterval(window, 8);
    ctx.fillStyle = '#555';
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'center';
    const firstTick = Math.ceil(this.zoomStart / tickInterval) * tickInterval;
    for (let t = firstTick; t <= this.zoomEnd; t += tickInterval) {
      const x = this.timeToX(t);
      ctx.fillStyle = '#444';
      ctx.fillRect(x, trackY + trackH, 1, 6);
      ctx.fillStyle = '#888';
      ctx.fillText(this.formatTime(t), x, h - 2);
    }

    // Playhead
    if (this.currentTime >= this.zoomStart && this.currentTime <= this.zoomEnd) {
      const px = this.timeToX(this.currentTime);
      ctx.strokeStyle = '#fff';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(px, 0);
      ctx.lineTo(px, h);
      ctx.stroke();

      // Playhead triangle
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.moveTo(px - 5, 0);
      ctx.lineTo(px + 5, 0);
      ctx.lineTo(px, 8);
      ctx.closePath();
      ctx.fill();
    }
  }

  private niceInterval(window: number, targetTicks: number): number {
    const raw = window / targetTicks;
    const nice = [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600];
    return nice.find(n => n >= raw) ?? raw;
  }

  private formatTime(secs: number): string {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    const s = Math.floor(secs % 60);
    if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
    return `${m}:${String(s).padStart(2, '0')}`;
  }

  @HostListener('click', ['$event'])
  onClick(e: MouseEvent) {
    if (this.duration <= 0) return;
    const rect = this.canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const t = this.xToTime(x);
    this.seek.emit(Math.max(0, Math.min(this.duration, t)));
  }

  // Arrow function so 'this' is preserved when used as event listener
  private readonly handleWheel = (e: WheelEvent) => {
    e.preventDefault();
    if (this.duration <= 0) return;

    const rect = this.canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const pivot = this.xToTime(x);

    const factor = e.deltaY > 0 ? 1.2 : 1 / 1.2;
    const visibleWindow = this.zoomEnd - this.zoomStart;
    const newWindow = Math.max(0.5, Math.min(this.duration, visibleWindow * factor));

    // Keep pivot point fixed under cursor
    const leftRatio = (pivot - this.zoomStart) / visibleWindow;
    this.zoomStart = pivot - leftRatio * newWindow;
    this.zoomEnd = this.zoomStart + newWindow;

    // Clamp to [0, duration]
    if (this.zoomStart < 0) { this.zoomEnd -= this.zoomStart; this.zoomStart = 0; }
    if (this.zoomEnd > this.duration) { this.zoomStart -= this.zoomEnd - this.duration; this.zoomEnd = this.duration; }
    this.zoomStart = Math.max(0, this.zoomStart);

    this.draw();
  };
}
