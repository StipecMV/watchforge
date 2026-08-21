import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { VideoPlayerComponent } from './video-player.component';
import { I18nService } from '../../i18n/i18n.service';
import { DetectionRegion } from '../../models/video.model';

function fakeCtx() {
  const ctx: Record<string, unknown> = {};
  for (const m of [
    'fillRect', 'clearRect', 'strokeRect', 'beginPath', 'moveTo', 'lineTo',
    'stroke', 'fill', 'closePath', 'fillText', 'measureText',
  ]) {
    ctx[m] = vi.fn();
  }
  ctx['measureText'] = vi.fn(() => ({ width: 10 }));
  ctx['canvas'] = { width: 0, height: 0 };
  return ctx as unknown as CanvasRenderingContext2D;
}

describe('VideoPlayerComponent (S6-3, clip player)', () => {
  let getCtx: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    getCtx = vi
      .spyOn(HTMLCanvasElement.prototype, 'getContext')
      .mockReturnValue(fakeCtx());
    await TestBed.configureTestingModule({
      imports: [VideoPlayerComponent],
    }).compileComponents();
    TestBed.inject(I18nService).setLocale('sk');
  });

  afterEach(() => getCtx.mockRestore());

  function createFixture(clipUrl: string | null) {
    const fixture = TestBed.createComponent(VideoPlayerComponent);
    fixture.componentRef.setInput('clipUrl', clipUrl);
    fixture.detectChanges();
    return fixture;
  }

  it('bez klipu zobrazí prázdny stav bez video elementu', () => {
    const fixture = createFixture(null);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('Vyberte udalosť');
    expect(el.querySelector('video')).toBeNull();
  });

  it('s clipUrl nastaví video src na streamovaciu URL klipu', () => {
    const fixture = createFixture('http://localhost:5000/api/v1/clips/11');
    const video = fixture.nativeElement.querySelector('video');
    expect(video?.getAttribute('src')).toBe('http://localhost:5000/api/v1/clips/11');
  });

  it('pri timeupdate emituje currentTime a duration', () => {
    const fixture = createFixture('http://localhost:5000/api/v1/clips/11');
    const comp = fixture.componentInstance;
    const times: number[] = [];
    comp.timeUpdate.subscribe(t => times.push(t));

    const video = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    Object.defineProperty(video, 'currentTime', { value: 12.5, configurable: true });
    Object.defineProperty(video, 'duration', { value: 30, configurable: true });
    video.dispatchEvent(new Event('timeupdate'));

    expect(times).toEqual([12.5]);
    expect(comp.duration).toBe(30);
  });

  it('togglePlay spúšťa/pozastavuje prehrávanie', () => {
    const fixture = createFixture('http://localhost:5000/api/v1/clips/11');
    const comp = fixture.componentInstance;
    const video = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    const play = vi.spyOn(video, 'play').mockResolvedValue(undefined);
    const pause = vi.spyOn(video, 'pause').mockImplementation(() => {});

    comp.togglePlay();
    expect(play).toHaveBeenCalled();

    video.dispatchEvent(new Event('play'));
    expect(comp.isPlaying).toBe(true);

    comp.togglePlay();
    expect(pause).toHaveBeenCalled();
  });

  it('seekTo nastaví currentTime videa', () => {
    const fixture = createFixture('http://localhost:5000/api/v1/clips/11');
    const comp = fixture.componentInstance;
    const video = fixture.nativeElement.querySelector('video') as HTMLVideoElement;
    Object.defineProperty(video, 'currentTime', { value: 0, writable: true, configurable: true });

    comp.seekTo(7.25);
    expect(video.currentTime).toBe(7.25);
  });

  it('s regiónom vykreslí bounding box na overlay canvas', () => {
    const fixture = createFixture('http://localhost:5000/api/v1/clips/11');
    const comp = fixture.componentInstance;
    const region: DetectionRegion = { x: 0.25, y: 0.5, width: 0.1, height: 0.2 };
    fixture.componentRef.setInput('region', region);
    fixture.detectChanges();

    comp.onTimeUpdate();
    const ctx = HTMLCanvasElement.prototype.getContext as ReturnType<typeof vi.spyOn>;
    const mock = ctx.mock.results[0].value as { strokeRect: ReturnType<typeof vi.fn> };
    expect(mock.strokeRect).toHaveBeenCalled();
  });

  it('formatTime formátuje sekundy na m:ss a h:mm:ss', () => {
    const fixture = createFixture(null);
    const comp = fixture.componentInstance;
    expect(comp.formatTime(65)).toBe('1:05');
    expect(comp.formatTime(3661)).toBe('1:01:01');
    expect(comp.formatTime(Number.NaN)).toBe('0:00');
  });
});
