import { Injectable, signal } from '@angular/core';

/**
 * Témy UI (S6-8): Light+Green (default), Dark+Purple, Contrast+Orange.
 * - Inicializácia: uložená voľba (localStorage 'wf-theme') > prefers-color-scheme.
 * - Aplikácia: data-theme atribút na <html> — styles.css má :root[data-theme=…].
 */
export type ThemeId = 'light-green' | 'dark-purple' | 'contrast-orange';

export const THEME_IDS: ThemeId[] = ['light-green', 'dark-purple', 'contrast-orange'];

const STORAGE_KEY = 'wf-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<ThemeId>(this.initialTheme());

  constructor() {
    this.apply();
  }

  /** Aktívna téma podľa localStorage, inak prefers-color-scheme (dark → Dark+Purple). */
  private initialTheme(): ThemeId {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (isThemeId(saved)) return saved;
    const prefersDark =
      typeof window !== 'undefined' &&
      window.matchMedia?.('(prefers-color-scheme: dark)').matches;
    return prefersDark ? 'dark-purple' : 'light-green';
  }

  setTheme(theme: ThemeId): void {
    this.theme.set(theme);
    localStorage.setItem(STORAGE_KEY, theme);
    this.apply();
  }

  toggleNext(): void {
    const idx = THEME_IDS.indexOf(this.theme());
    this.setTheme(THEME_IDS[(idx + 1) % THEME_IDS.length]);
  }

  private apply(): void {
    if (typeof document !== 'undefined') {
      document.documentElement.dataset['theme'] = this.theme();
    }
  }
}

export function isThemeId(value: string | null): value is ThemeId {
  return value === 'light-green' || value === 'dark-purple' || value === 'contrast-orange';
}
