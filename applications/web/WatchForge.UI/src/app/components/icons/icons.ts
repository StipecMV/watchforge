/**
 * WatchForge UI — ikony a avatary (S6-5, design handoff).
 *
 * 40 location ikon (20 outdoor + 20 indoor) a 10 avatar glyfov.
 * SVG pathy — stroke štýl, re-color cez currentColor
 * (CSS vars pre témy S6-8).
 */

export interface SvgPath {
  d: string;
  /** 'none' = stroke (default), 'currentColor' = vyplnená plocha (malé detaily). */
  fill?: 'none' | 'currentColor';
  fillOpacity?: number;
}

export const LOCATION_GROUPS: Record<'outdoor' | 'indoor', string[]> = {
  outdoor: [
    'garden', 'yard', 'field', 'fence', 'gate', 'driveway', 'pool', 'garage_ext',
    'mailbox', 'shed', 'path', 'trees', 'parking', 'terrace', 'balcony', 'porch',
    'sidewalk', 'deck', 'greenhouse', 'bench',
  ],
  indoor: [
    'hallway', 'kitchen', 'living', 'bedroom', 'bathroom', 'garage_int', 'office', 'basement',
    'attic', 'stairs', 'dining', 'laundry', 'pantry', 'closet', 'entrance', 'study',
    'foyer', 'nursery', 'gym', 'server',
  ],
};

/** Počet ikon — design hovorí 40 (20+20). */
export const LOCATION_ICON_COUNT = 40;

export const LOCATION_ICON_PATHS: Record<string, SvgPath[]> = {
  // ── outdoor ────────────────────────────────────────────────────────────
  garden: [{ d: 'M3 20h18M6 20c0-3 2-5 4-5M14 20c0-3 2-5 4-5M10 11c0-3 1-5 2-7 1 2 2 4 2 7-2 1-2 2-2 4-2-2-2-3-2-4z' }],
  yard: [{ d: 'M3 20h18M5 20v-6M9 20v-4M13 20v-7M17 20v-5M21 20v-3' }],
  field: [{ d: 'M3 18h18M3 14h18M3 10h18M6 21l1-15M12 21V6M18 21l-1-15' }],
  fence: [{ d: 'M3 21V8l3-3 3 3v13M15 21V8l3-3 3 3v13M3 12h18M3 17h18' }],
  gate: [{ d: 'M3 21V6h18v15M3 9l18 6M21 9l-18 6' }],
  driveway: [{ d: 'M4 21l4-18M20 21l-4-18M8 11h8M9 16h6' }],
  pool: [{ d: 'M3 17c2-2 4 0 6 0s4-2 6 0 4 0 6 0M3 13c2-2 4 0 6 0s4-2 6 0 4 0 6 0M7 13V4h2v9M15 13V4h2v9' }],
  garage_ext: [{ d: 'M3 21V10l9-6 9 6v11M6 21v-9h12v9M6 17h12' }],
  mailbox: [{ d: 'M5 21V11h10c2 0 4 1 4 4v6M5 11V8M9 11v4M19 15h-9M14 4h-2v4h2c1 0 2-1 2-2s-1-2-2-2z' }],
  shed: [{ d: 'M3 21V9l9-5 9 5v12M9 21v-7h6v7M3 9h18' }],
  path: [{ d: 'M4 21c2-4 4-2 5-6s3-2 5-6 4-2 6-5' }, { d: 'M4 18l3 1M9 14l3 1M14 9l3 1' }],
  trees: [{ d: 'M7 17c-2 0-4-2-4-4s2-4 3-4c0-2 2-3 3-3s3 1 3 3c1 0 3 2 3 4s-2 4-4 4M7 17v4M19 21c-3 0-5-2-5-5l1-1 1 1c0-2 1-3 3-3s3 1 3 3l1-1 1 1c0 3-2 5-5 5M19 21v-7' }],
  parking: [{ d: 'M4 4h16v16H4z' }, { d: 'M10 16V8h3a2 2 0 0 1 0 4h-3' }],
  terrace: [{ d: 'M3 21V14h18v7M3 14l4-4h10l4 4M7 21v-7M12 21v-7M17 21v-7' }],
  balcony: [{ d: 'M3 17h18v4H3zM5 17v-4M9 17v-4M13 17v-4M17 17v-4M3 13h18M7 13V7h10v6' }],
  porch: [{ d: 'M3 21V11l9-7 9 7v10M6 21v-6h12v6M9 21v-3M15 21v-3' }],
  sidewalk: [{ d: 'M3 20l6-16M21 20l-6-16M9 20l4-16M9 4l4 16' }],
  deck: [{ d: 'M3 17h18M3 13h18M3 9h18M5 17v4M11 17v4M17 17v4M7 5v4M14 5v4' }],
  greenhouse: [{ d: 'M3 21V11l9-7 9 7v10M3 11h18M12 4v17M7 14v7M17 14v7' }],
  bench: [{ d: 'M3 11h18v3H3zM3 11V8h18v3M5 14v6M19 14v6M5 8V5M19 8V5' }],
  // ── indoor ────────────────────────────────────────────────────────────
  hallway: [{ d: 'M5 3v18M19 3v18M5 3h14M5 21h14M9 12v-2M15 12v-2' }],
  kitchen: [{ d: 'M3 21h18M5 21V8h14v13M5 8h14M9 12v5M13 12v5M17 12v5M7 5V3M11 5V3M15 5V3M19 5V3' }],
  living: [{ d: 'M3 17v-4a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v4M3 17h18v3H3zM6 11V8a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v3' }],
  bedroom: [{ d: 'M3 18V8M21 18V8M3 18h18M3 14h18M6 14v-3a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v3' }],
  bathroom: [{ d: 'M4 12V6a2 2 0 1 1 4 0M3 12h18v3a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4zM7 19l-1 2M17 19l1 2' }],
  garage_int: [{ d: 'M3 5h18v16H3zM3 9h18M3 13h18M3 17h18M9 5v16M15 5v16' }],
  office: [{ d: 'M4 21V8h16v13M4 8l8-5 8 5M8 21v-7h8v7M11 17h2' }],
  basement: [{ d: 'M3 4h18l-2 16H5zM7 8h10M8 12h8M9 16h6' }],
  attic: [{ d: 'M3 21l9-17 9 17M7 21v-6h10v6M11 15v-3M13 15v-3' }],
  stairs: [{ d: 'M4 20h4v-4h4v-4h4V8h4V4M4 20h16' }],
  dining: [{ d: 'M3 12h2M19 12h2M12 3v2M12 19v2' }, { d: 'M12 12m-6 0a6 6 0 1 0 12 0a6 6 0 1 0-12 0' }],
  laundry: [
    { d: 'M4 3h16v18H4z' },
    { d: 'M12 10m-4 0a4 4 0 1 0 8 0a4 4 0 1 0-8 0', fill: 'currentColor', fillOpacity: 0.25 },
    { d: 'M8 7m-0.7 0a0.7 0.7 0 1 0 1.4 0a0.7 0.7 0 1 0-1.4 0', fill: 'currentColor' },
    { d: 'M16 7m-0.7 0a0.7 0.7 0 1 0 1.4 0a0.7 0.7 0 1 0-1.4 0', fill: 'currentColor' },
  ],
  pantry: [{ d: 'M5 3h14v18H5zM5 9h14M5 15h14M9 3v18' }],
  closet: [{ d: 'M5 3h14v18H5zM12 3v18M5 12h7M12 8l3 3M19 12h-7M12 16l3-3' }],
  entrance: [{ d: 'M6 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16M6 21h12M14 12h.5' }],
  study: [{ d: 'M4 4h16v13H4zM4 17l-1 4h18l-1-4M8 8h8M8 12h5' }],
  foyer: [{ d: 'M4 21V8l8-5 8 5v13M9 21v-7h6v7M4 12h2M18 12h2' }],
  nursery: [{ d: 'M12 6m-3 0a3 3 0 1 0 6 0a3 3 0 1 0-6 0' }, { d: 'M5 21c1-4 4-6 7-6s6 2 7 6M9 7l-1-2M15 7l1-2' }],
  gym: [{ d: 'M3 12h18M5 9v6M9 7v10M15 7v10M19 9v6' }],
  server: [
    { d: 'M4 3h16v6H4zM4 11h16v6H4z' },
    { d: 'M8 6m-0.7 0a0.7 0.7 0 1 0 1.4 0a0.7 0.7 0 1 0-1.4 0', fill: 'currentColor' },
    { d: 'M8 14m-0.7 0a0.7 0.7 0 1 0 1.4 0a0.7 0.7 0 1 0-1.4 0', fill: 'currentColor' },
    { d: 'M11 6h6M11 14h6' },
  ],
};

/** Štandardná ikona pre neznámy názov (fallback). */
export const FALLBACK_LOCATION_ICON: SvgPath[] = [{ d: 'M12 6m-6 0a6 6 0 1 0 12 0a6 6 0 1 0-12 0' }];

/** Avatar glyfy (10) — farebný štvorček + glyf (design avatars.jsx). */
export interface AvatarSpec {
  id: string; // av01..av10
  name: string;
  glyph: string;
  /** CSS var pozadia (re-theme cez témy S6-8). */
  bg: string;
  paths: SvgPath[];
}

export const AVATAR_SPECS: AvatarSpec[] = [
  { id: 'av01', name: 'Anvil', glyph: 'anvil', bg: 'var(--wf-brand)', paths: [{ d: 'M3 9h13l-2 4 2 2H6l2-2-3-2zM12 15v4M9 19h6' }] },
  { id: 'av02', name: 'Eye', glyph: 'eye', bg: 'var(--wf-motion)', paths: [{ d: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z' }, { d: 'M12 9m-3 0a3 3 0 1 0 6 0a3 3 0 1 0-6 0', fill: 'currentColor' }] },
  { id: 'av03', name: 'Shield', glyph: 'shield', bg: 'var(--wf-ok)', paths: [{ d: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3z' }, { d: 'M9 12l2 2 4-4' }] },
  { id: 'av04', name: 'Flame', glyph: 'flame', bg: 'var(--wf-motion2)', paths: [{ d: 'M12 3c2 4 5 5 5 9a5 5 0 1 1-10 0c0-2 1-3 2-5 1 2 2 2 3 0 0-2 0-3 0-4z', fill: 'currentColor', fillOpacity: 0.18 }] },
  { id: 'av05', name: 'Gear', glyph: 'gear', bg: 'var(--wf-brand)', paths: [{ d: 'M12 9m-3 0a3 3 0 1 0 6 0a3 3 0 1 0-6 0' }, { d: 'M19 12a7 7 0 0 0-.1-1.2l2-1.5-2-3.4-2.3.9a7 7 0 0 0-2-1.2L14 3h-4l-.6 2.6a7 7 0 0 0-2 1.2L5 5.9 3 9.3l2 1.5A7 7 0 0 0 5 12a7 7 0 0 0 .1 1.2L3 14.7l2 3.4 2.3-.9a7 7 0 0 0 2 1.2L10 21h4l.6-2.6a7 7 0 0 0 2-1.2l2.3.9 2-3.4-2-1.5c.1-.4.1-.8.1-1.2z' }] },
  { id: 'av06', name: 'Lock', glyph: 'lock', bg: 'var(--wf-motion)', paths: [{ d: 'M5 11h14v9H5z' }, { d: 'M8 11V7a4 4 0 0 1 8 0v4' }] },
  { id: 'av07', name: 'Hammer', glyph: 'hammer', bg: 'var(--wf-ok)', paths: [{ d: 'M14 4l6 6-3 3-6-6 3-3z', fill: 'currentColor', fillOpacity: 0.15 }, { d: 'M11 7l-7 7 3 3 7-7' }] },
  { id: 'av08', name: 'Compass', glyph: 'compass', bg: 'var(--wf-motion2)', paths: [{ d: 'M12 3m-9 0a9 9 0 1 0 18 0a9 9 0 1 0-18 0' }, { d: 'M15 9l-2 4-4 2 2-4 4-2z', fill: 'currentColor', fillOpacity: 0.3 }] },
  { id: 'av09', name: 'Bell', glyph: 'bellA', bg: 'var(--wf-brand)', paths: [{ d: 'M6 8a6 6 0 0 1 12 0c0 7 3 7 3 9H3c0-2 3-2 3-9z' }, { d: 'M10 21a2 2 0 0 0 4 0' }] },
  { id: 'av10', name: 'Key', glyph: 'key', bg: 'var(--wf-motion)', paths: [{ d: 'M8 8m-4 0a4 4 0 1 0 8 0a4 4 0 1 0-8 0' }, { d: 'M12 12h9M17 12v3M21 12v3' }] },
];

export const AVATAR_COUNT = 10;

/** Vráti ikonové pathy pre názov lokácie (fallback pri neznámom). */
export function locationIconPaths(name: string): SvgPath[] {
  return LOCATION_ICON_PATHS[name] ?? FALLBACK_LOCATION_ICON;
}

/** Vráti avatar spec podľa id (av01..av10, fallback prvý). */
export function avatarSpec(id: string): AvatarSpec {
  return AVATAR_SPECS.find(a => a.id === id) ?? AVATAR_SPECS[0];
}
