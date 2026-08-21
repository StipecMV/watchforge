import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ThemeService, THEME_IDS } from './theme.service';

/**
 * S6-8: témy — Light+Green / Dark+Purple / Contrast+Orange,
 * prefers-color-scheme fallback, localStorage perzistencia.
 */

describe('ThemeService (S6-8)', () => {
  let service: ThemeService;

  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
    TestBed.configureTestingModule({});
    service = TestBed.inject(ThemeService);
  });

  afterEach(() => {
    delete document.documentElement.dataset['theme'];
    localStorage.clear();
  });

  /** jsdom nemá matchMedia — definuje ho ako mock (prefers-color-scheme). */
  function mockPrefersDark(dark: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: dark } as MediaQueryList),
    });
  }

  it('default bez uloženej voľby + light prefers → light-green', () => {
    mockPrefersDark(false);
    // nová inštancia (service je singleton — vytvoríme čerstvý po mockeri)
    const fresh = new ThemeService();
    expect(fresh.theme()).toBe('light-green');
    expect(document.documentElement.dataset['theme']).toBe('light-green');
  });

  it('prefers-color-scheme dark → dark-purple', () => {
    mockPrefersDark(true);
    const fresh = new ThemeService();
    expect(fresh.theme()).toBe('dark-purple');
  });

  it('setTheme aplikuje data-theme na <html> a uloží do localStorage', () => {
    service.setTheme('contrast-orange');
    expect(service.theme()).toBe('contrast-orange');
    expect(document.documentElement.dataset['theme']).toBe('contrast-orange');
    expect(localStorage.getItem('wf-theme')).toBe('contrast-orange');
  });

  it('uložená voľba má prednosť pred prefers-color-scheme', () => {
    localStorage.setItem('wf-theme', 'light-green');
    mockPrefersDark(true); // systém je dark, ale user si vybral light
    const fresh = new ThemeService();
    expect(fresh.theme()).toBe('light-green');
  });

  it('toggleNext cykluje light-green → dark-purple → contrast-orange → light-green', () => {
    service.setTheme('light-green');
    service.toggleNext();
    expect(service.theme()).toBe('dark-purple');
    service.toggleNext();
    expect(service.theme()).toBe('contrast-orange');
    service.toggleNext();
    expect(service.theme()).toBe('light-green');
  });

  it('THEME_IDS obsahuje presne tri témy z kanbanu', () => {
    expect(THEME_IDS).toEqual(['light-green', 'dark-purple', 'contrast-orange']);
  });
});
