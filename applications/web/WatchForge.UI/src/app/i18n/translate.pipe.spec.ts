import { describe, it, expect } from 'vitest';
import { TranslatePipe } from './translate.pipe';
import { I18nService } from './i18n.service';

describe('TranslatePipe', () => {
  function makePipe(): TranslatePipe {
    return new TranslatePipe(new I18nService());
  }

  it('transformuje kľúč cez i18n service (SK default)', () => {
    const pipe = makePipe();
    expect(pipe.transform('common.retry')).toBe('Skúsiť znova');
  });

  it('rešpektuje aktuálny locale service', () => {
    const service = new I18nService();
    service.setLocale('en');
    const pipe = new TranslatePipe(service);
    expect(pipe.transform('common.retry')).toBe('Retry');
  });

  it('podporuje parametre', () => {
    const pipe = makePipe();
    expect(pipe.transform('common.hello', { name: 'User Two' })).toBe('Ahoj, User Two!');
  });

  it('je pure pipe: rovnaký vstup → rovnaký výstup pri nezmenenom locale', () => {
    const pipe = makePipe();
    expect(pipe.transform('sidebar.noVideos')).toBe(pipe.transform('sidebar.noVideos'));
  });
});
