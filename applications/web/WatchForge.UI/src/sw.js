/* WatchForge PWA service worker — cache-first pre statické asset,
   sieť pre API (live view NESMIE byť cacheovaný). */
const CACHE = 'watchforge-v1';

self.addEventListener('install', (e) => {
  self.skipWaiting();
});

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))),
  );
  self.clients.claim();
});

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);

  // API a live stream — vždy sieť (čerstvé dáta, žiadna cache)
  if (url.pathname.startsWith('/api/')) return;

  // Navigácia (HTML) — sieť s cache fallbackom na index.html (offline)
  if (e.request.mode === 'navigate') {
    e.respondWith(
      fetch(e.request).catch(() => caches.match('/index.html')),
    );
    return;
  }

  // Statické assety — cache-first
  e.respondWith(
    caches.match(e.request).then((hit) => {
      if (hit) return hit;
      return fetch(e.request).then((resp) => {
        if (resp.ok && (url.pathname.includes('/icons/') || url.pathname.includes('.svg'))) {
          const copy = resp.clone();
          caches.open(CACHE).then((c) => c.put(e.request, copy));
        }
        return resp;
      });
    }),
  );
});
