// Service Worker der Einsatzueberwachung (1.11): macht die EL-Ansicht als App installierbar und zeigt bei Funkloch
// eine eigene Seite statt der Fehlerseite des Browsers. Inhalte der App werden bewusst NICHT zwischengespeichert –
// Einsatzdaten kommen immer frisch vom Server (Blazor), angemeldete Inhalte bleiben aus dem Speicher des Geraets.
// Ausnahme: Kartenkacheln (oeffentliche Karten, keine Einsatzdaten) – damit die Karte bei schwachem Netz/Funkloch
// fuer bereits angesehene oder vorab gesicherte Gebiete weiter angezeigt wird.
const CACHE = 'einsatz-offline-v2';
const TILE_CACHE = 'einsatz-kacheln-v1';
const OFFLINE_FILES = ['/offline.html', '/icons/icon-192.png', '/js/el-outbox.js'];

// Nur Kartenanbieter mit CORS (Antworten lassen sich normal speichern). Google-Kacheln werden nicht gespeichert.
const TILE_HOSTS = /^([abc]\.)?tile\.openstreetmap\.org$|^([abc]\.)?tile\.opentopomap\.org$|^server\.arcgisonline\.com$/;
const TILE_TIMEOUT_MS = 4000;
const MAX_TILES = 5000;

self.addEventListener('install', (event) => {
    event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(OFFLINE_FILES)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(keys.filter((k) => k !== CACHE && k !== TILE_CACHE).map((k) => caches.delete(k))))
            .then(() => self.clients.claim()));
});

self.addEventListener('fetch', (event) => {
    const request = event.request;

    // Seitenaufrufe: ohne Netz die Offline-Seite statt „Safari kann die Seite nicht öffnen“
    if (request.mode === 'navigate') {
        event.respondWith(fetch(request).catch(() => caches.match('/offline.html')));
        return;
    }

    if (request.method === 'GET' && TILE_HOSTS.test(new URL(request.url).hostname)) {
        event.respondWith(tile(request));
    }
});

// Kachel: zuerst aus dem Netz (aktuelle Karte, Cache-Regeln der Anbieter), bei Fehler oder langsamem Netz aus dem Speicher
async function tile(request) {
    const cache = await caches.open(TILE_CACHE);
    const key = tileKey(request.url);
    const network = fetch(request).then((response) => {
        if (response.ok && response.type === 'cors') {
            cache.put(key, response.clone()).then(trimTiles).catch(() => { });
        }
        return response;
    });

    const cached = await cache.match(key);
    if (!cached) return network;

    // Gespeicherte Kachel vorhanden: hoechstens kurz auf das Netz warten, Fehlerantworten nie der Kachel vorziehen
    const timeout = new Promise((resolve) => setTimeout(() => resolve(cached), TILE_TIMEOUT_MS));
    return Promise.race([network.then((r) => (r.ok ? r : cached), () => cached), timeout]);
}

// a./b./c.-Subdomains derselben Kachel nur einmal speichern
function tileKey(url) {
    return url.replace(/:\/\/[abc]\.tile\./, '://tile.');
}

let trimming = false;
async function trimTiles() {
    if (trimming) return;
    trimming = true;
    try {
        const cache = await caches.open(TILE_CACHE);
        const keys = await cache.keys();
        // Aelteste zuerst entfernen (Reihenfolge = Reihenfolge des Speicherns)
        for (let i = 0; i < keys.length - MAX_TILES; i++) {
            await cache.delete(keys[i]);
        }
    } finally {
        trimming = false;
    }
}
