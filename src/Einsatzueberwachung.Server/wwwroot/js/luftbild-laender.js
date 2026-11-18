// Amtliche Luftbilder der Länder (DOP20, Issue 210) als eine Leaflet-Ebene „Luftbild (Länder)“.
// Geladen werden nur die Länder, die der sichtbare Ausschnitt berührt – an Landesgrenzen mehrere übereinander
// (die Dienste liefern außerhalb ihres Landes durchsichtige Bilder). Liste: /data/luftbild-laender.json,
// dieselbe nutzt der PDF-Bericht auf dem Server.
window.LuftbildLaender = (function () {
    let quellen = null;
    const laden = fetch('/data/luftbild-laender.json')
        .then((r) => (r.ok ? r.json() : []))
        .then((q) => { quellen = q; })
        .catch(() => { quellen = []; });
    const jahr = new Date().getFullYear();

    const Ebene = L.LayerGroup.extend({
        onAdd: function (map) {
            L.LayerGroup.prototype.onAdd.call(this, map);
            this._teile = {};
            map.on('moveend', this._aktualisieren, this);
            laden.then(() => this._aktualisieren());
        },
        onRemove: function (map) {
            map.off('moveend', this._aktualisieren, this);
            L.LayerGroup.prototype.onRemove.call(this, map);
            this.clearLayers();
            this._teile = {};
        },
        _aktualisieren: function () {
            const map = this._map;
            if (!map || !quellen) return;
            const sicht = map.getBounds();
            for (const q of quellen) {
                const grenzen = L.latLngBounds([q.box[1], q.box[0]], [q.box[3], q.box[2]]);
                const vorhanden = this._teile[q.land];
                if (sicht.intersects(grenzen) && !vorhanden) {
                    const optionen = {
                        layers: q.ebenen, format: 'image/png', transparent: true, version: q.version,
                        bounds: grenzen, maxZoom: 20,
                        attribution: 'Luftbild ' + q.name + ': ' + q.quelle.replace('{jahr}', jahr)
                    };
                    // Nur Dienste mit CORS: sonst wuerde der Browser die Kacheln gar nicht laden
                    if (q.cors) optionen.crossOrigin = 'anonymous';
                    const teil = L.tileLayer.wms(q.url, optionen);
                    this._teile[q.land] = teil;
                    this.addLayer(teil);
                } else if (!sicht.intersects(grenzen) && vorhanden) {
                    this.removeLayer(vorhanden);
                    delete this._teile[q.land];
                }
            }
        }
    });

    return { ebene: () => new Ebene() };
})();
