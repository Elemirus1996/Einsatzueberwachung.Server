// Funkloch-Puffer der Einsatzleitung (1.11): Funksprueche und Notizen auf dem Geraet erfassen, wenn die Verbindung
// zum Server weg ist, und automatisch nachsenden, sobald sie zurueck ist (POST /api/el-outbox).
// Laeuft ohne Blazor – in der App (Knopf in der Verbindungsleiste) und auf der Offline-Seite (offline.html).
(function () {
    'use strict';

    var QUEUE_KEY = 'einsatz.elOutbox';
    var TEAMS_KEY = 'einsatz.elTeams';
    var RETRY_MS = 15000;
    var flushing = false;
    var retryTimer = null;
    var authProblem = false;

    function load(key, fallback) {
        try { return JSON.parse(localStorage.getItem(key)) || fallback; } catch (e) { return fallback; }
    }

    function save(key, value) {
        try { localStorage.setItem(key, JSON.stringify(value)); } catch (e) { /* Speicher voll/gesperrt */ }
    }

    function queue() { return load(QUEUE_KEY, []); }

    function newId() {
        if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
        return 'x' + Date.now().toString(36) + Math.random().toString(36).slice(2);
    }

    function esc(text) {
        return String(text).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function time(iso) {
        var d = new Date(iso);
        return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
    }

    // ── Nachsenden ───────────────────────────────────────────────
    function scheduleRetry() {
        clearTimeout(retryTimer);
        if (queue().some(function (e) { return !e.error; })) retryTimer = setTimeout(flush, RETRY_MS);
    }

    function flush() {
        if (flushing) return;
        var pending = queue().filter(function (e) { return !e.error; });
        if (pending.length === 0) { render(); return; }
        flushing = true;
        var entry = pending[0];

        fetch('/api/el-outbox', {
            method: 'POST',
            credentials: 'same-origin',
            redirect: 'manual', // abgelaufene Anmeldung leitet sonst unbemerkt auf die Login-Seite um
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: entry.id, kind: entry.kind, text: entry.text, recipient: entry.recipient || null, createdAt: entry.createdAt })
        }).then(function (r) {
            if (r.ok) {
                authProblem = false;
                save(QUEUE_KEY, queue().filter(function (e) { return e.id !== entry.id; }));
                return true;
            }
            if (r.type === 'opaqueredirect' || r.status === 401 || r.status === 403) {
                authProblem = true;
                return false;
            }
            if (r.status === 400 || r.status === 409) {
                // Nicht zustellbar (z. B. Einsatz inzwischen beendet): behalten und anzeigen, nicht endlos wiederholen
                return r.json().catch(function () { return {}; }).then(function (body) {
                    save(QUEUE_KEY, queue().map(function (e) {
                        if (e.id === entry.id) e.error = body.message || body.error || ('Fehler ' + r.status);
                        return e;
                    }));
                    return true;
                });
            }
            return false; // Serverfehler: spaeter erneut
        }).catch(function () {
            return false; // kein Netz
        }).then(function (goOn) {
            flushing = false;
            render();
            if (goOn) flush(); else scheduleRetry();
        });
    }

    function enqueue(kind, text, recipient) {
        var list = queue();
        list.push({ id: newId(), kind: kind, text: text, recipient: recipient || '', createdAt: new Date().toISOString() });
        save(QUEUE_KEY, list);
        render();
        flush();
    }

    function discard(id) {
        save(QUEUE_KEY, queue().filter(function (e) { return e.id !== id; }));
        render();
        renderDialogList();
    }

    // ── Anzeige: Hinweis „x Meldungen warten“ ────────────────────
    function ensureStyles() {
        if (document.getElementById('el-outbox-style')) return;
        var css =
            // Ueber der Tab-Leiste der EL-Ansicht (ca. 60 px), damit deren Beschriftung lesbar bleibt
            '.elo-pill{position:fixed;left:12px;bottom:calc(72px + env(safe-area-inset-bottom));z-index:3001;display:none;' +
            'align-items:center;gap:8px;min-height:44px;padding:8px 14px;border:0;border-radius:22px;background:#b8821a;color:#14181f;' +
            'font:600 14px/1.3 system-ui,-apple-system,"Segoe UI",sans-serif;box-shadow:0 2px 8px rgba(0,0,0,.3)}' +
            '.elo-pill.is-error{background:#a72920;color:#fff}' +
            '.elo-dialog{width:min(560px,calc(100vw - 24px));max-height:calc(100dvh - 24px);padding:0;border:0;border-radius:10px;' +
            'background:#faf7f0;color:#14181f;font:15px/1.4 system-ui,-apple-system,"Segoe UI",sans-serif}' +
            '.elo-dialog::backdrop{background:rgba(10,13,18,.6)}' +
            '.elo-head{display:flex;align-items:center;gap:8px;padding:12px 16px;background:#14181f;color:#f0e9d8;font-weight:700}' +
            '.elo-head span{flex:1}.elo-head button{min-width:44px;min-height:44px;border:0;background:none;color:inherit;font-size:20px}' +
            '.elo-body{display:flex;flex-direction:column;gap:10px;padding:16px}' +
            '.elo-hint{margin:0;font-size:13px;color:#4a525e}' +
            '.elo-seg{display:grid;grid-template-columns:1fr 1fr;border:1px solid rgba(20,24,31,.4);border-radius:6px;overflow:hidden}' +
            '.elo-seg button{min-height:48px;border:0;background:#faf7f0;color:#14181f;font:inherit;font-weight:700}' +
            '.elo-seg button[aria-pressed="true"]{background:#14181f;color:#f0e9d8}' +
            '.elo-input{width:100%;min-height:48px;box-sizing:border-box;padding:10px 12px;border:1px solid rgba(20,24,31,.4);' +
            'border-radius:6px;background:#fff;color:#14181f;font:16px/1.3 system-ui,-apple-system,"Segoe UI",sans-serif}' +
            'textarea.elo-input{min-height:110px;resize:vertical}' +
            '.elo-send{min-height:56px;border:0;border-radius:6px;background:#a72920;color:#fff;font:inherit;font-weight:700;font-size:16px}' +
            '.elo-send:disabled{opacity:.5}' +
            '.elo-list{margin:0;padding:0;list-style:none;display:flex;flex-direction:column;gap:6px}' +
            '.elo-item{padding:8px 10px;border:1px solid rgba(20,24,31,.22);border-radius:6px;background:#fff;font-size:14px}' +
            '.elo-item.is-error{border-color:#a72920}' +
            '.elo-meta{display:flex;justify-content:space-between;gap:8px;font-size:12px;color:#4a525e}' +
            '.elo-err{color:#a72920;font-weight:600;font-size:13px}' +
            '.elo-item button{margin-top:6px;min-height:36px;padding:4px 12px;border:1px solid rgba(20,24,31,.4);border-radius:4px;' +
            'background:none;font:inherit;font-size:13px}';
        var style = document.createElement('style');
        style.id = 'el-outbox-style';
        style.textContent = css;
        document.head.appendChild(style);
    }

    function render() {
        ensureStyles();
        var pill = document.getElementById('el-outbox-pill');
        if (!pill) {
            pill = document.createElement('button');
            pill.id = 'el-outbox-pill';
            pill.type = 'button';
            pill.className = 'elo-pill';
            pill.addEventListener('click', open);
            document.body.appendChild(pill);
        }

        var list = queue();
        var waiting = list.filter(function (e) { return !e.error; }).length;
        var failed = list.length - waiting;
        if (list.length === 0) { pill.style.display = 'none'; return; }

        pill.style.display = 'inline-flex';
        pill.classList.toggle('is-error', failed > 0 || authProblem);
        if (authProblem) {
            pill.textContent = '⚠ ' + waiting + ' Meldung(en) warten – bitte neu anmelden';
        } else if (failed > 0) {
            pill.textContent = '⚠ ' + failed + ' Meldung(en) nicht zustellbar – ansehen';
        } else {
            pill.textContent = '⏳ ' + waiting + (waiting === 1 ? ' Meldung wartet' : ' Meldungen warten') + ' auf Verbindung';
        }
    }

    // ── Erfassen-Dialog ──────────────────────────────────────────
    var dialog = null;
    var kind = 'funk';

    function buildDialog() {
        ensureStyles();
        dialog = document.createElement('dialog');
        dialog.className = 'elo-dialog';
        dialog.setAttribute('aria-label', 'Funk oder Notiz erfassen');
        dialog.innerHTML =
            '<div class="elo-head"><span>Funk / Notiz erfassen</span>' +
            '<button type="button" data-elo="close" aria-label="Schließen">✕</button></div>' +
            '<div class="elo-body">' +
            '<p class="elo-hint">Ohne Verbindung wird der Eintrag auf diesem Gerät gespeichert und automatisch nachgesendet ' +
            '– mit Vermerk der Erfassungszeit.</p>' +
            '<div class="elo-seg" role="group" aria-label="Art">' +
            '<button type="button" data-elo-kind="funk" aria-pressed="true">Funk</button>' +
            '<button type="button" data-elo-kind="notiz" aria-pressed="false">Notiz</button></div>' +
            '<select class="elo-input" data-elo="recipient" aria-label="Empfänger"></select>' +
            '<textarea class="elo-input" data-elo="text" aria-label="Text" placeholder="Meldung …" maxlength="2000"></textarea>' +
            '<button type="button" class="elo-send" data-elo="send">Speichern &amp; senden</button>' +
            '<ul class="elo-list" data-elo="list"></ul>' +
            '</div>';
        document.body.appendChild(dialog);

        dialog.addEventListener('click', function (ev) {
            var t = ev.target.closest('button');
            if (!t) return;
            if (t.dataset.elo === 'close') dialog.close();
            if (t.dataset.eloKind) setKind(t.dataset.eloKind);
            if (t.dataset.elo === 'send') send();
            if (t.dataset.eloDiscard) discard(t.dataset.eloDiscard);
        });
    }

    function setKind(value) {
        kind = value;
        dialog.querySelectorAll('[data-elo-kind]').forEach(function (b) {
            b.setAttribute('aria-pressed', String(b.dataset.eloKind === value));
        });
        dialog.querySelector('[data-elo="recipient"]').style.display = value === 'funk' ? '' : 'none';
    }

    function fillRecipients() {
        var select = dialog.querySelector('[data-elo="recipient"]');
        var teams = load(TEAMS_KEY, []);
        select.innerHTML = '<option value="">— An alle —</option>' + teams.map(function (name) {
            return '<option value="' + esc(name) + '">' + esc(name) + '</option>';
        }).join('');
    }

    function renderDialogList() {
        if (!dialog) return;
        var list = queue();
        dialog.querySelector('[data-elo="list"]').innerHTML = list.map(function (e) {
            return '<li class="elo-item' + (e.error ? ' is-error' : '') + '">' +
                '<div class="elo-meta"><span>' + (e.kind === 'funk' ? 'Funk' : 'Notiz') +
                (e.recipient ? ' an ' + esc(e.recipient) : '') + '</span><span>' + time(e.createdAt) + '</span></div>' +
                '<div>' + esc(e.text) + '</div>' +
                (e.error ? '<div class="elo-err">Nicht zustellbar: ' + esc(e.error) + '</div>' +
                    '<button type="button" data-elo-discard="' + esc(e.id) + '">Verwerfen</button>'
                    : '<div class="elo-meta"><span>wartet auf Verbindung</span></div>') +
                '</li>';
        }).join('');
    }

    function send() {
        var area = dialog.querySelector('[data-elo="text"]');
        var text = area.value.trim();
        if (!text) { area.focus(); return; }
        var recipient = kind === 'funk' ? dialog.querySelector('[data-elo="recipient"]').value : '';
        enqueue(kind, text, recipient);
        area.value = '';
        renderDialogList();
    }

    function open() {
        if (!dialog) buildDialog();
        fillRecipients();
        setKind(kind);
        renderDialogList();
        if (!dialog.open) dialog.showModal();
        dialog.querySelector('[data-elo="text"]').focus();
    }

    window.elOutbox = {
        open: open,
        flush: flush,
        // Von der EL-Ansicht aufgerufen, damit die Empfaengerliste auch offline die aktuellen Teams kennt
        setTeams: function (names) { save(TEAMS_KEY, (names || []).slice(0, 100)); }
    };

    window.addEventListener('online', flush);
    document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'visible') flush(); });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', flush);
    else flush();
})();
