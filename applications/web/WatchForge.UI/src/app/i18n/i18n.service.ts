import { Injectable, signal } from '@angular/core';
import { TRANSLATIONS, Locale, TranslationParams } from './translations';

/**
 * Lightweight i18n service (NFR-08).
 *
 * - Default jazyk: slovenčina (`sk`), backup: angličtina (`en`).
 * - Chýbajúci kľúč v aktuálnom jazyku → fallback do druhého jazyka → samotný kľúč.
 * - Texty sú externalizované v `translations.ts`; prepnutie jazyka cez `setLocale`
 *   nemení žiadnu aplikačnú logiku.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  readonly locale = signal<Locale>('sk');

  setLocale(locale: Locale): void {
    this.locale.set(locale);
  }

  /**
   * Vráti preložený text pre kľúč, s voliteľnou substitúciou parametrov
   * `{name}` → hodnota z `params`.
   */
  translate(key: string, params?: TranslationParams): string {
    const current = this.locale();
    const other: Locale = current === 'sk' ? 'en' : 'sk';

    let text = TRANSLATIONS[current][key];
    if (text === undefined) {
      text = TRANSLATIONS[other][key];
    }
    if (text === undefined) {
      return key;
    }

    if (params) {
      for (const [name, value] of Object.entries(params)) {
        text = text.replaceAll(`{${name}}`, String(value));
      }
    }
    return text;
  }
}
