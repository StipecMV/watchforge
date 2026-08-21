import { describe, it, expect, beforeEach } from 'vitest';
import { I18nService } from './i18n.service';

describe('I18nService', () => {
  let service: I18nService;

  beforeEach(() => {
    service = new I18nService();
  });

  it('default locale je slovenčina (sk)', () => {
    expect(service.locale()).toBe('sk');
  });

  it('translate vráti slovenský text pre default locale', () => {
    expect(service.translate('common.retry')).toBe('Skúsiť znova');
  });

  it('setLocale("en") prepne jazyk a translate vráti anglický text', () => {
    service.setLocale('en');
    expect(service.locale()).toBe('en');
    expect(service.translate('common.retry')).toBe('Retry');
  });

  it('prepnutie späť na sk funguje bez straty stavu', () => {
    service.setLocale('en');
    service.setLocale('sk');
    expect(service.locale()).toBe('sk');
    expect(service.translate('common.retry')).toBe('Skúsiť znova');
  });

  it('chýbajúci kľúč v EN slovníku fallback na SK hodnotu', () => {
    service.setLocale('en');
    // 'sk.only.key' existuje len v SK slovníku
    expect(service.translate('sk.only.key')).toBe('Slovenská hodnota');
  });

  it('chýbajúci kľúč v oboch jazykoch vráti samotný kľúč', () => {
    expect(service.translate('neexistujuci.kluc')).toBe('neexistujuci.kluc');
  });

  it('podporuje parametre {placeholder} v texte', () => {
    expect(service.translate('common.hello', { name: 'Admin' })).toBe('Ahoj, Admin!');
  });

  it('podporuje parametre aj v EN locale', () => {
    service.setLocale('en');
    expect(service.translate('common.hello', { name: 'User One' })).toBe('Hello, User One!');
  });

  it('translate nemení vstupný slovník (žiadna mutácia)', () => {
    const sk1 = service.translate('common.retry');
    service.setLocale('en');
    const en1 = service.translate('common.retry');
    service.setLocale('sk');
    expect(service.translate('common.retry')).toBe(sk1);
    expect(en1).not.toBe(sk1);
  });
});
