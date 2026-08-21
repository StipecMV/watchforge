import { Component, Input } from '@angular/core';
import { locationIconPaths } from './icons';

/**
 * Location ikona (S6-5, design handoff): 40 scén (20 outdoor + 20 indoor)
 * ako stroke SVG re-color cez currentColor. Používa sa v Settings → Cameras
 * (mapping ikona) a neskôr v Cameras panely.
 */
@Component({
  selector: 'app-location-icon',
  standalone: true,
  template: `
    <svg
      [attr.width]="size"
      [attr.height]="size"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      @for (p of paths; track $index) {
        <path [attr.d]="p.d" [attr.fill]="p.fill ?? 'none'" [attr.fill-opacity]="p.fillOpacity ?? null" />
      }
    </svg>
  `,
})
export class LocationIconComponent {
  @Input() name = 'camera';
  @Input() size = 18;

  get paths() {
    return locationIconPaths(this.name);
  }
}
