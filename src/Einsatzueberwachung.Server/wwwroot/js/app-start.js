// Start der Blazor-Verbindung mit Einstellungen fuer Mobilfunk (1.11):
//  - beliebig viele Wiederverbindungsversuche, anfangs schnell, dann alle 5 s
//  - ist die Sitzung auf dem Server abgelaufen, wird die Seite automatisch neu geladen (Daten liegen auf dem Server)
//  - Service Worker fuer „als App installieren“ und die Offline-Seite (nur fuer die Einsatzleitung, nicht im Team-Zugang)
(function () {
    Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: 100000,
                retryIntervalMilliseconds: function (attempt) {
                    return attempt < 5 ? 1000 : 5000;
                }
            }
        }
    });

    var modal = document.getElementById('components-reconnect-modal');
    if (modal) {
        new MutationObserver(function () {
            if (modal.classList.contains('components-reconnect-rejected')) {
                location.reload();
            }
        }).observe(modal, { attributes: true, attributeFilter: ['class'] });
    }

    if ('serviceWorker' in navigator && document.documentElement.dataset.pwa === '1') {
        navigator.serviceWorker.register('/sw.js').catch(function () { /* ohne gueltiges Zertifikat nicht moeglich */ });
    }
})();
