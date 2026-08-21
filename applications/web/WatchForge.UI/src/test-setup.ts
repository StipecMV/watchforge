import '@angular/compiler';
import '@analogjs/vitest-angular/setup-zone';

import {
  BrowserTestingModule,
  platformBrowserTesting,
} from '@angular/platform-browser/testing';
import { getTestBed } from '@angular/core/testing';

getTestBed().initTestEnvironment(
  BrowserTestingModule,
  platformBrowserTesting(),
);

// ---- jsdom chýbajúce API (globálne mocky pre komponentové testy) ----

// ResizeObserver (timeline komponent ho používa na redraw pri resize).
class ResizeObserverMock {
  observe() {}
  unobserve() {}
  disconnect() {}
}
(globalThis as Record<string, unknown>)['ResizeObserver'] = ResizeObserverMock;

// Canvas 2D context (timeline/player overlay kreslia cez getContext('2d')).
// jsdom bez node-canvas vracia null — Proxy stub vráti no-op funkcie.
const canvasCtxStub = new Proxy(
  { canvas: { width: 0, height: 0 } },
  {
    get: (target, prop) => {
      if (prop in target) return (target as Record<string, unknown>)[prop as string];
      return () => {};
    },
    set: () => true,
  },
);
HTMLCanvasElement.prototype.getContext = (() =>
  canvasCtxStub) as unknown as typeof HTMLCanvasElement.prototype.getContext;
