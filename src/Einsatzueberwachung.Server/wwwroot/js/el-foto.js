// Foto der Einsatzleitung in den Messenger-Chat des Divera-Einsatzes (1.12): Kamera des Handys oeffnen, Bild im
// Browser verkleinern (schnell auch im Mobilfunk), Vorschau mit optionalem Text, senden an POST /api/divera/foto.
// Laeuft ohne Blazor; schlaegt das Senden fehl (Funkloch), bleibt das Foto im Dialog fuer einen neuen Versuch.
(function () {
    'use strict';

    var MAX_KANTE = 1920;
    var QUALITAET = 0.82;
    var dialog = null;
    var bild = null; // { blob, url }

    function verkleinern(datei) {
        var laden = window.createImageBitmap
            ? createImageBitmap(datei, { imageOrientation: 'from-image' }).catch(function () { return ladeAlsBild(datei); })
            : ladeAlsBild(datei);
        return laden.then(function (quelle) {
            var b = quelle.width, h = quelle.height;
            var faktor = Math.min(1, MAX_KANTE / Math.max(b, h));
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(b * faktor);
            canvas.height = Math.round(h * faktor);
            canvas.getContext('2d').drawImage(quelle, 0, 0, canvas.width, canvas.height);
            return new Promise(function (resolve, reject) {
                canvas.toBlob(function (blob) { blob ? resolve(blob) : reject(new Error('Bild nicht lesbar')); }, 'image/jpeg', QUALITAET);
            });
        });
    }

    function ladeAlsBild(datei) {
        return new Promise(function (resolve, reject) {
            var img = new Image();
            img.onload = function () { resolve(img); };
            img.onerror = function () { reject(new Error('Bild nicht lesbar')); };
            img.src = URL.createObjectURL(datei);
        });
    }

    function baueDialog() {
        dialog = document.createElement('dialog');
        dialog.className = 'elf-dialog';
        dialog.setAttribute('aria-label', 'Foto an den Divera-Einsatz-Chat');
        dialog.innerHTML =
            '<div class="elf-head"><span>Foto an Divera-Einsatz-Chat</span>' +
            '<button type="button" data-elf="close" aria-label="Schließen">✕</button></div>' +
            '<div class="elf-body">' +
            '<img class="elf-vorschau" data-elf="vorschau" alt="Vorschau" />' +
            '<textarea class="elf-input" data-elf="text" maxlength="500" placeholder="Text zum Foto (optional) …"></textarea>' +
            '<p class="elf-status" data-elf="status" role="status"></p>' +
            '<button type="button" class="elf-senden" data-elf="senden">Senden</button>' +
            '</div>';
        document.body.appendChild(dialog);
        dialog.addEventListener('click', function (ev) {
            var knopf = ev.target.closest('button');
            if (!knopf) return;
            if (knopf.dataset.elf === 'close') schliessen();
            if (knopf.dataset.elf === 'senden') senden();
        });
        dialog.addEventListener('close', aufraeumen);
    }

    function status(text, fehler) {
        var el = dialog.querySelector('[data-elf="status"]');
        el.textContent = text || '';
        el.classList.toggle('is-error', !!fehler);
    }

    function aufraeumen() {
        if (bild) URL.revokeObjectURL(bild.url);
        bild = null;
    }

    function schliessen() {
        if (dialog && dialog.open) dialog.close();
    }

    function senden() {
        if (!bild) return;
        var knopf = dialog.querySelector('[data-elf="senden"]');
        knopf.disabled = true;
        status('Wird gesendet …', false);
        var form = new FormData();
        form.append('foto', bild.blob, 'foto.jpg');
        form.append('text', dialog.querySelector('[data-elf="text"]').value.trim());

        fetch('/api/divera/foto', { method: 'POST', body: form, credentials: 'same-origin', redirect: 'manual' })
            .then(function (r) {
                if (r.ok) return { ok: true };
                if (r.type === 'opaqueredirect') return { ok: false, text: 'Anmeldung abgelaufen – Seite neu laden.' };
                return r.json().catch(function () { return {}; }).then(function (b) {
                    return { ok: false, text: b.error || b.message || ('Fehler ' + r.status) };
                });
            })
            .catch(function () { return { ok: false, text: 'Keine Verbindung – bitte erneut senden.' }; })
            .then(function (ergebnis) {
                knopf.disabled = false;
                if (ergebnis.ok) {
                    status('✓ Foto im Einsatz-Chat.', false);
                    setTimeout(schliessen, 1500);
                } else {
                    status(ergebnis.text, true);
                    knopf.textContent = 'Erneut senden';
                }
            });
    }

    function aufnehmen() {
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/*';
        input.setAttribute('capture', 'environment');
        input.addEventListener('change', function () {
            var datei = input.files && input.files[0];
            if (!datei) return;
            if (!dialog) baueDialog();
            aufraeumen();
            dialog.querySelector('[data-elf="text"]').value = '';
            dialog.querySelector('[data-elf="senden"]').textContent = 'Senden';
            dialog.querySelector('[data-elf="vorschau"]').removeAttribute('src');
            status('Foto wird vorbereitet …', false);
            if (!dialog.open) dialog.showModal();
            verkleinern(datei).then(function (blob) {
                bild = { blob: blob, url: URL.createObjectURL(blob) };
                dialog.querySelector('[data-elf="vorschau"]').src = bild.url;
                status(Math.round(blob.size / 1024) + ' KB', false);
            }).catch(function (e) {
                status(e.message || 'Foto konnte nicht gelesen werden.', true);
            });
        });
        input.click();
    }

    window.elFoto = { aufnehmen: aufnehmen };
})();
