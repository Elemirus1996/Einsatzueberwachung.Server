// Service Worker der Einsatzueberwachung (1.11): macht die EL-Ansicht als App installierbar und zeigt bei Funkloch
// eine eigene Seite statt der Fehlerseite des Browsers. Inhalte der App werden bewusst NICHT zwischengespeichert –
// Einsatzdaten kommen immer frisch vom Server (Blazor), angemeldete Inhalte bleiben aus dem Speicher des Geraets.
const CACHE = 'einsatz-offline-v2';
const OFFLINE_FILES = ['/offline.html', '/icons/icon-192.png', '/js/el-outbox.js'];

self.addEventListener('install', (event) => {
    event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(OFFLINE_FILES)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
            .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (event) => {
    // Nur Seitenaufrufe: ohne Netz die Offline-Seite statt „Safari kann die Seite nicht öffnen“
    if (event.request.mode !== 'navigate') return;
    event.respondWith(fetch(event.request).catch(() => caches.match('/offline.html')));
});
