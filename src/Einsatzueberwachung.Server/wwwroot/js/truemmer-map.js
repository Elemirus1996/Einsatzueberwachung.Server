// Trümmer-Lagekarte: pixel-basierte Leaflet-Map.
// Bildkoordinaten = LatLng [y, x] in CRS.Simple (Origin top-left, y nach unten zählt deshalb negativ).
//
// JS-Interop-API:
//   window.truemmerMap.init(elementId, dotnetRef)
//   window.truemmerMap.loadKarte(elementId, { id, imageUrl, width, height })
//   window.truemmerMap.renderAreas(elementId, areas)            -> areas: [{id, name, color, points:[{x,y}], assignedTeamName}]
//   window.truemmerMap.startDraw(elementId)                     -> aktiviert Polygon-Zeichnen
//   window.truemmerMap.cancelDraw(elementId)
//   window.truemmerMap.dispose(elementId)
//
// Ruft DotNet bei abgeschlossenem Polygon zurück: OnPolygonCreated([{X,Y},...])

(function () {
    const instances = new Map();

    // Bildkoordinate (x, y in Pixeln) → Leaflet LatLng [y_inverted, x]
    // y wird invertiert, damit (0,0) oben-links liegt (Leaflet rechnet sonst y nach unten als negativ).
    function pxToLatLng(x, y, height) {
        return [height - y, x];
    }

    function latLngToPx(latlng, height) {
        return { x: latlng.lng, y: height - latlng.lat };
    }

    function init(elementId, dotnetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;

        if (instances.has(elementId)) {
            dispose(elementId);
        }

        const map = L.map(el, {
            crs: L.CRS.Simple,
            minZoom: -4,
            maxZoom: 4,
            zoomControl: true,
            attributionControl: false
        });

        const inst = {
            map: map,
            dotnetRef: dotnetRef,
            imageLayer: null,
            areasLayer: L.featureGroup().addTo(map),
            drawControl: null,
            currentDraw: null,
            height: 0,
            width: 0
        };
        instances.set(elementId, inst);
    }

    function loadKarte(elementId, info) {
        const inst = instances.get(elementId);
        if (!inst) return;

        if (inst.imageLayer) {
            inst.map.removeLayer(inst.imageLayer);
        }

        const w = info.width;
        const h = info.height;
        inst.width = w;
        inst.height = h;

        const bounds = [[0, 0], [h, w]];
        inst.imageLayer = L.imageOverlay(info.imageUrl, bounds).addTo(inst.map);
        inst.map.setMaxBounds([[ -h * 0.2, -w * 0.2 ], [ h * 1.2, w * 1.2 ]]);
        inst.map.fitBounds(bounds);

        // Areas-Layer leeren — werden via renderAreas neu gesetzt
        inst.areasLayer.clearLayers();
    }

    function renderAreas(elementId, areas) {
        const inst = instances.get(elementId);
        if (!inst) return;
        inst.areasLayer.clearLayers();

        areas.forEach(a => {
            if (!a.points || a.points.length < 2) return;
            const ring = a.points.map(p => pxToLatLng(p.x, p.y, inst.height));
            const poly = L.polygon(ring, {
                color: a.color || "#FF9800",
                weight: 3,
                fillOpacity: 0.2
            });
            const tooltip = a.assignedTeamName
                ? `<strong>${escapeHtml(a.name)}</strong><br/>Team: ${escapeHtml(a.assignedTeamName)}`
                : `<strong>${escapeHtml(a.name)}</strong>`;
            poly.bindTooltip(tooltip, { sticky: true });
            poly.addTo(inst.areasLayer);
        });
    }

    function startDraw(elementId) {
        const inst = instances.get(elementId);
        if (!inst) return;
        cancelDraw(elementId);

        // Wie auf der Einsatzkarte (Leaflet.draw): Eckpunkte, gefuellte Vorschau, Schliessen per Klick auf den
        // ersten Punkt, keine Ueberschneidungen. Keine Flaechenangabe – das Bild hat keinen Massstab.
        const dotnetRef = inst.dotnetRef;
        const height = inst.height;
        let created = false;

        const handler = new L.Draw.Polygon(inst.map, {
            allowIntersection: false,
            showArea: false,
            showLength: false,
            drawError: {
                color: "#e74c3c",
                message: "<strong>Fehler!</strong> Linien dürfen sich nicht überschneiden."
            },
            shapeOptions: {
                color: "#3388ff",
                weight: 3,
                fillOpacity: 0.2
            }
        });

        function onCreated(e) {
            created = true;
            const px = e.layer.getLatLngs()[0].map(p => latLngToPx(p, height));
            if (dotnetRef && px.length >= 3) {
                dotnetRef.invokeMethodAsync("OnPolygonCreated", px).catch(() => { /* ignore */ });
            }
        }

        function onStop() {
            const abgebrochen = !created;
            cleanup();
            // Abbruch per Esc → Seite setzt den Zeichenmodus zurueck
            if (abgebrochen && dotnetRef) {
                dotnetRef.invokeMethodAsync("OnDrawCanceled").catch(() => { /* ignore */ });
            }
        }

        function cleanup() {
            inst.map.off(L.Draw.Event.CREATED, onCreated);
            inst.map.off(L.Draw.Event.DRAWSTOP, onStop);
            inst.currentDraw = null;
            handler.disable();
        }

        inst.map.on(L.Draw.Event.CREATED, onCreated);
        inst.map.on(L.Draw.Event.DRAWSTOP, onStop);
        handler.enable();
        inst.currentDraw = { cleanup: cleanup };
    }

    function cancelDraw(elementId) {
        const inst = instances.get(elementId);
        if (!inst) return;
        if (inst.currentDraw) inst.currentDraw.cleanup();
    }

    function dispose(elementId) {
        const inst = instances.get(elementId);
        if (!inst) return;
        try { inst.map.remove(); } catch { /* ignore */ }
        instances.delete(elementId);
    }

    function escapeHtml(s) {
        if (s == null) return "";
        return String(s)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    window.truemmerMap = {
        init,
        loadKarte,
        renderAreas,
        startDraw,
        cancelDraw,
        dispose
    };
})();
