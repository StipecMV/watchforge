import { Component, Input } from '@angular/core';
import { avatarSpec } from './icons';

/**
 * Avatar (S6-5, design handoff avatars.jsx): 10 pre-generovaných glyfov.
 * Farebný zaoblený štvorček (bg z CSS var — re-theme cez témy) + glyf.
 */
@Component({
  selector: 'app-avatar',
  standalone: true,
  template: `
    <div
      class="avatar"
      [style.width.px]="size"
      [style.height.px]="size"
      [style.border-radius.px]="Math.round(size * 0.27)"
      [style.background]="spec.bg"
      [title]="spec.name"
    >
      <svg
        [attr.width]="Math.round(size * 0.6)"
        [attr.height]="Math.round(size * 0.6)"
        viewBox="0 0 24 24"
        fill="none"
        stroke="var(--wf-surface)"
        stroke-width="1.7"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        @for (p of spec.paths; track $index) {
          <path [attr.d]="p.d" [attr.fill]="p.fill ?? 'none'" [attr.fill-opacity]="p.fillOpacity ?? null" />
        }
      </svg>
    </div>
  `,
  styles: [
    `
      .avatar {
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }
    `,
  ],
})
export class AvatarComponent {
  @Input() id = 'av01';
  @Input() size = 36;

  readonly Math = Math;

  get spec() {
    return avatarSpec(this.id);
  }
}
