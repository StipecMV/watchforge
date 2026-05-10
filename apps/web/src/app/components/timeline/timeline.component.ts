import {
  AfterViewInit, Component, ElementRef, EventEmitter, Input,
  OnChanges, OnDestroy, Output, SimpleChanges, ViewChild
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

  @Input() duration = 0;
  @Input() currentTime = 0;
  @Input() events: DetectionEvent[] = [];
  @Output() seek = new EventEmitter<number>();

  private zoomStart = 0;
  private zoomEnd = 0;

  // Pan state
  private isPanning = false;
  private panStartX = 0;
  private panStartZoomStart = 0;
  private didPan = false;

  // Scrollbar thumb drag
  private thumbDragging = false;
  private thumbDragStartX = 0;
  private thumbDragStartZoomStart = 0;

  private resizeObserver?: ResizeObserver;
  private boundHandleWheel = this.handleWheel.bind(this);
  private boundThumbMouseMove = this.onThumbMove.bind(this);
  private boundThumbMouseUp = this.onThumbUp.bind(this);

  // ---- lifecycle ----

  ngAfterViewInit() {
    this.resizeObserver = new ResizeObserver(() => this.draw());
    this.resizeObserver.observe(this.canvas);
    this.canvas.addEventListener('wheel', this.boundHandleWheel, { passive: false });
    this.draw();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['duration'] && this.duration > 0) {
      this.zoomStart = 0;
      this.zoomEnd = this.duration;
    }
    this.draw();
  }

  ngOnDestroy() {
    this.resizeObserver?.disconnect();
    this.canvas.removeEventListener('wheel', this.boundHandleWheel);
    window.removeEventListener('mousemove', this.boundThumbMouseMove);
    window.removeEventListener('mouseup', this.boundThumbMouseUp);
  }

  // ---- getters ----

  private get canvas(): HTMLCanvasElement {
    return this.canvasRef.nativeElement;
  }

  get isZoomed(): boolean {
    return this.duration > 0 && (this.zoomEnd - this.zoomStart) < this.duration * 0.999;
  }

  get thumbLeft(): number {
    if (!this.duration) return 0;
    return (this.zoomStart / this.duration) * 100;
  }

  get thumbWidth(): number {
    if (!this.duration) return 100;
    return ((this.zoomEnd - this.zoomStart) / this.duration) * 100;
  }

  // ---- zoom buttons ----

  zoomIn() {
    this.applyZoom(1 / 1.5, this.currentTime);
  }

  zoomOut() {
    this.applyZoom(1.5, this.currentTime);
  }

  resetZoom() {
    this.zoomStart = 0;
    this.zoomEnd = this.duration;
    this.draw();
  }

  private applyZoom(factor: number, pivot: number) {
    if (this.duration <= 0) return;
    pivot = Math.max(this.zoomStart, Math.min(this.zoomEnd, pivot));
    const visWindow = this.zoomEnd - this.zoomStart;
    const newWindow = Math.max(0.5, Math.min(this.duration, visWindow * factor));
    const leftRatio = (pivot - this.zoomStart) / visWindow;
    this.zoomStart = pivot - leftRatio * newWindow;
    this.zoomEnd = this.zoomStart + newWindow;
    this.clampZoom();
    this.draw();
  }

  private clampZoom() {
    if (this.zoomStart < 0) { this.zoomEnd -= this.zoomStart; this.zoomStart = 0; }
    if (this.zoomEnd > this.duration) { this.zoomStart -= this.zoomEnd - this.duration; this.zoomEnd = this.duration; }
    this.zoomStart = Math.max(0, this.zoomStart);
    this.zoomEnd = Math.min(this.duration, this.zoomEnd);
  }

  // ---- canvas mouse events (click + drag pan) ----

  onMouseDown(e: MouseEvent) {
    if (e.button !== 0) return;
    this.isPanning = true;
    this.didPan = false;
    this.panStartX = e.clientX;
    this.panStartZoomStart = this.zoomStart;
  }

  onMouseMove(e: MouseEvent) {
    if (!this.isPanning) return;
    const dx = e.clientX - this.panStartX;
    if (Math.abs(dx) > 3) this.didPan = true;
    if (!this.didPan) return;

    const w = this.canvas.clientWidth || 1;
    const visWindow = this.zoomEnd - this.zoomStart;
    const dtSec = -(dx / w) * visWindow;
    this.zoomStart = this.panStartZoomStart + dtSec;
    this.zoomEnd = this.zoomStart + visWindow;
    this.clampZoom();
    this.draw();
  }

  onMouseUp(_e: MouseEvent) {
    this.isPanning = false;
  }

  onClick(e: MouseEvent) {
    if (this.didPan || this.duration <= 0) return;
    const rect = this.canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const t = this.xToTime(x);
    this.seek.emit(Math.max(0, Math.min(this.duration, t)));
  }

  // ---- scrollbar ----

  onScrollbarClick(e: MouseEvent) {
    if (this.thumbDragging) return;
    const track = e.currentTarget as HTMLElement;
    const rect = track.getBoundingClientRect();
    const ratio = (e.clientX - rect.left) / rect.width;
    const visWindow = this.zoomEnd - this.zoomStart;
    this.zoomStart = ratio * this.duration - visWindow / 2;
    this.zoomEnd = this.zoomStart + visWindow;
    this.clampZoom();
    this.draw();
  }

  onThumbDown(e: MouseEvent) {
    e.stopPropagation();
    this.thumbDragging = true;
    this.thumbDragStartX = e.clientX;
    this.thumbDragStartZoomStart = this.zoomStart;
    window.addEventListener('mousemove', this.boundThumbMouseMove);
    window.addEventListener('mouseup', this.boundThumbMouseUp);
  }

  private onThumbMove(e: MouseEvent) {
    if (!this.thumbDragging) return;
    const dx = e.clientX - this.thumbDragStartX;
    const trackEl = this.canvas.closest('.timeline-wrap')
      ?.querySelector('.scrollbar-track') as HTMLElement | null;
    const trackW = trackEl?.clientWidth || 1;
    const dtSec = (dx / trackW) * this.duration;
    const visWindow = this.zoomEnd - this.zoomStart;
    this.zoomStart = this.thumbDragStartZoomStart + dtSec;
    this.zoomEnd = this.zoomStart + visWindow;
    this.clampZoom();
    this.draw();
  }

  private onThumbUp(_e: MouseEvent) {
    this.thumbDragging = false;
    window.removeEventListener('mousemove', this.boundThumbMouseMove);
    window.removeEventListener('mouseup', this.boundThumbMouseUp);
  }

  // ---- wheel zoom ----

  private handleWheel(e: WheelEvent) {
    e.preventDefault();
    if (this.duration <= 0) return;
    const rect = this.canvas.getBoundingClientRect();
    const pivot = this.xToTime(e.clientX - rect.left);
    const factor = e.deltaY > 0 ? 1.2 : 1 / 1.2;
    this.applyZoom(factor, pivot);
  }

  // ---- draw ----

  draw() {
    const canvas = this.canvas;
    const ctx = canvas.getContext('2d')!;
    const w = canvas.width = canvas.offsetWidth || 800;
    const h = canvas.height = canvas.offsetHeight || 52;

    ctx.fillStyle = '#1a1a1a';
    ctx.fillRect(0, 0, w, h);

    if (this.duration <= 0) {
      ctx.fillStyle = '#555';
      ctx.font = '12px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('No video loaded', w / 2, h / 2 + 4);
      return;
    }

    // Track
    const trackY = Math.floor(h * 0.2);
    const trackH = Math.floor(h * 0.45);
    ctx.fillStyle = '#2a2a2a';
    ctx.fillRect(0, trackY, w, trackH);

    // Motion events (red segments) — drawn as spans using durationMs
    ctx.fillStyle = 'rgba(220, 50, 50, 0.85)';
    const visWindow = this.zoomEnd - this.zoomStart;
    for (const ev of this.events) {
      const startT = ev.timestampMs / 1000;
      const endT = startT + (ev.durationMs ?? 500) / 1000;
      // Skip if completely outside the visible window
      if (endT < this.zoomStart || startT > this.zoomEnd) continue;
      const x1 = this.timeToX(Math.max(startT, this.zoomStart));
      const x2 = this.timeToX(Math.min(endT, this.zoomEnd));
      const segW = Math.max(2, x2 - x1);
      ctx.fillRect(x1, trackY, segW, trackH);
    }

    // Tick marks & time labels
    const tickInterval = this.niceInterval(visWindow, 8);
    const firstTick = Math.ceil(this.zoomStart / tickInterval) * tickInterval;
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'center';
    for (let t = firstTick; t <= this.zoomEnd; t += tickInterval) {
      const x = this.timeToX(t);
      ctx.fillStyle = '#444';
      ctx.fillRect(x, trackY + trackH, 1, 5);
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
      ctx.fillStyle = '#fff';
      ctx.beginPath();
      ctx.moveTo(px - 5, 0);
      ctx.lineTo(px + 5, 0);
      ctx.lineTo(px, 8);
      ctx.closePath();
      ctx.fill();
    }
  }

  // ---- helpers ----

  private timeToX(time: number): number {
    const w = this.canvas.width;
    const visWindow = this.zoomEnd - this.zoomStart;
    if (visWindow <= 0) return 0;
    return ((time - this.zoomStart) / visWindow) * w;
  }

  private xToTime(x: number): number {
    const w = this.canvas.width;
    const visWindow = this.zoomEnd - this.zoomStart;
    return this.zoomStart + (x / w) * visWindow;
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
}
