import { Pipe, PipeTransform } from '@angular/core';
import { I18nService } from './i18n.service';
import { TranslationParams } from './translations';

/**
 * Pure pipe pre šablóny: `{{ 'common.retry' | translate }}`
 * alebo s parametrami: `{{ 'common.hello' | translate: { name: user } }}`.
 */
@Pipe({
  name: 'translate',
  pure: true,
  standalone: true,
})
export class TranslatePipe implements PipeTransform {
  constructor(private readonly i18n: I18nService) {}

  transform(key: string, params?: TranslationParams): string {
    return this.i18n.translate(key, params);
  }
}
