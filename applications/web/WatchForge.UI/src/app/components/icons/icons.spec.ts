import { describe, it, expect } from 'vitest';
import {
  AVATAR_COUNT,
  AVATAR_SPECS,
  LOCATION_GROUPS,
  LOCATION_ICON_COUNT,
  LOCATION_ICON_PATHS,
  avatarSpec,
  locationIconPaths,
} from './icons';

describe('icons (S6-5, design handoff)', () => {
  it('má presne 40 location ikon (20 outdoor + 20 indoor)', () => {
    const all = [...LOCATION_GROUPS.outdoor, ...LOCATION_GROUPS.indoor];
    expect(all).toHaveLength(40);
    expect(LOCATION_ICON_COUNT).toBe(40);
  });

  it('každá ikona zo skupín má definované SVG pathy', () => {
    const all = [...LOCATION_GROUPS.outdoor, ...LOCATION_GROUPS.indoor];
    for (const name of all) {
      const paths = LOCATION_ICON_PATHS[name];
      expect(paths, `ikona ${name}`).toBeDefined();
      expect(paths.length).toBeGreaterThan(0);
      for (const p of paths) {
        expect(p.d.length).toBeGreaterThan(0);
      }
    }
  });

  it('locationIconPaths vráti fallback pre neznámy názov', () => {
    expect(locationIconPaths('neexistuje')).toEqual([{ d: 'M12 6m-6 0a6 6 0 1 0 12 0a6 6 0 1 0-12 0' }]);
  });

  it('má presne 10 avatarov s glyfom aj farbou pozadia', () => {
    expect(AVATAR_SPECS).toHaveLength(10);
    expect(AVATAR_COUNT).toBe(10);
    for (const spec of AVATAR_SPECS) {
      expect(spec.id).toMatch(/^av\d{2}$/);
      expect(spec.paths.length).toBeGreaterThan(0);
      expect(spec.bg).toMatch(/^var\(--wf-/);
    }
  });

  it('avatarSpec vráti fallback pre neznáme id', () => {
    expect(avatarSpec('av99').id).toBe('av01');
    expect(avatarSpec('av05').id).toBe('av05');
  });
});
