// Live-Tracking von GPS-Halsbändern auf der Leaflet-Karte
// Zeichnet Polylines (Pfade) für zugewiesene Halsbänder und zeigt Warnungen bei Out-of-Bounds

// ── UTM-Konvertierung (WGS84 → UTM) ──────────────────────────────────────────
function latLngToUtm(lat, lng) {
    const a = 6378137.0, f = 1 / 298.257223563;
    const b = a * (1 - f);
    const e2 = 1 - (b * b) / (a * a);
    const e2p = e2 / (1 - e2);
    const k0 = 0.9996;
    const zone = Math.floor((lng + 180) / 6) + 1;
    const lon0 = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180;
    const latR = lat * Math.PI / 180;
    const lngR = lng * Math.PI / 180;
    const N = a / Math.sqrt(1 - e2 * Math.sin(latR) ** 2);
    const T = Math.tan(latR) ** 2;
    const C = e2p * Math.cos(latR) ** 2;
    const A = Math.cos(latR) * (lngR - lon0);
    const n = (a - b) / (a + b);
    const M = a * ((1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256) * latR
        - (3 * e2 / 8 + 3 * e2 ** 2 / 32 + 45 * e2 ** 3 / 1024) * Math.sin(2 * latR)
        + (15 * e2 ** 2 / 256 + 45 * e2 ** 3 / 1024) * Math.sin(4 * latR)
        - 35 * e2 ** 3 / 3072 * Math.sin(6 * latR));
    const easting = k0 * N * (A + (1 - T + C) * A ** 3 / 6
        + (5 - 18 * T + T ** 2 + 72 * C - 58 * e2p) * A ** 5 / 120) + 500000;
    const northing = k0 * (M + N * Math.tan(latR) * (A ** 2 / 2
        + (5 - T + 9 * C + 4 * C ** 2) * A ** 4 / 24
        + (61 - 58 * T + T ** 2 + 600 * C - 330 * e2p) * A ** 6 / 720))
        + (lat < 0 ? 10000000 : 0);
    const band = 'CDEFGHJKLMNPQRSTUVWXX'[Math.min(Math.floor((lat + 80) / 8), 20)];
    return `${zone}${band} ${Math.round(easting)} ${Math.round(northing)}`;
}

window.CollarTracking = {
    // Gespeicherte Tracking-Daten pro Halsband
    _tracks: {},

    // Konfiguriertes Halsband-Marker-Symbol ("paw" | "dog" | "bone" | "dot")
    _collarIcon: 'paw',
    _trackColorMode: 'black',
    _markerColorMode: 'area-black-outline',

    _resolveColor: function (areaColor, mode) {
        if (mode === 'black' || mode === 'black-white-outline') return '#000000';
        if (mode !== 'contrast' || !/^#[0-9a-f]{6}$/i.test(areaColor)) return areaColor;

        const red = parseInt(areaColor.slice(1, 3), 16);
        const green = parseInt(areaColor.slice(3, 5), 16);
        const blue = parseInt(areaColor.slice(5, 7), 16);
        return `#${(255 - red).toString(16).padStart(2, '0')}${(255 - green).toString(16).padStart(2, '0')}${(255 - blue).toString(16).padStart(2, '0')}`;
    },

    _resolveTrackColor: function (areaColor) {
        return this._trackColorMode === 'area-dots' || this._trackColorMode === 'black-cased'
            ? '#000000'
            : this._resolveColor(areaColor, this._trackColorMode);
    },

    _createTrackOutline: function (positions, opacity) {
        if (this._trackColorMode !== 'area-cased' && this._trackColorMode !== 'black-cased') return null;

        return L.polyline(positions, {
            color: this._trackColorMode === 'area-cased' ? '#000000' : '#ffffff',
            weight: 7,
            opacity: opacity,
            lineCap: 'round',
            lineJoin: 'round',
            interactive: false
        });
    },

    _createTrackDotOverlay: function (positions, areaColor, opacity) {
        if (this._trackColorMode !== 'area-dots') return null;

        return L.polyline(positions, {
            color: this._resolveColor(areaColor, 'area'),
            weight: 6,
            opacity: opacity,
            dashArray: '1 17',
            lineCap: 'round',
            interactive: false
        });
    },

    // Gibt Font-Awesome-Icon-HTML basierend auf Konfiguration und Farbe zurück
    _getCollarIconHtml: function (color) {
        const outline = this._markerColorMode === 'area-black-outline'
            ? '-webkit-text-stroke:1.5px #000;paint-order:stroke fill;'
            : this._markerColorMode === 'area-white-outline' || this._markerColorMode === 'black-white-outline'
                ? '-webkit-text-stroke:1.5px #fff;paint-order:stroke fill;'
                : '';
        const style = `font-size:24px;color:${color};${outline}filter:drop-shadow(0 1px 3px rgba(0,0,0,0.55));display:block;line-height:1;`;
        switch (this._collarIcon) {
            case 'paw':
                return `<i class="fa-solid fa-paw" style="${style}"></i>`;
            case 'dog':
                return `<i class="fa-solid fa-dog" style="${style}"></i>`;
            case 'bone':
                return `<i class="fa-solid fa-bone" style="${style}"></i>`;
            case 'crosshairs':
                return `<i class="fa-solid fa-crosshairs" style="${style}"></i>`;
            case 'location-arrow':
                return `<i class="fa-solid fa-location-arrow" style="${style}"></i>`;
            case 'dot':
            default:
                return `<i class="fa-solid fa-location-dot" style="${style}"></i>`;
        }
    },

    // Setzt Darstellungsoptionen (wird direkt nach initialize() aufgerufen)
    setOptions: function (opts) {
        if (opts && opts.collarIcon) this._collarIcon = opts.collarIcon;
        if (opts && ['area', 'black', 'contrast', 'area-dots', 'area-cased', 'black-cased'].includes(opts.trackColorMode)) this._trackColorMode = opts.trackColorMode;
        if (opts && ['area', 'black', 'contrast', 'area-black-outline', 'area-white-outline', 'black-white-outline'].includes(opts.markerColorMode)) this._markerColorMode = opts.markerColorMode;
    },

    // Erstellt das divIcon für einen Halsband-Marker (konfiguriertes Symbol, optional mit OOB-Pulsring)
    _createCollarIcon: function (color, oobActive) {
        const iconHtml = this._getCollarIconHtml(color);
        if (oobActive) {
            return L.divIcon({
                html: `<div style="position:relative;width:36px;height:36px;display:flex;align-items:center;justify-content:center;">` +
                      `<svg xmlns="http://www.w3.org/2000/svg" width="36" height="36" viewBox="0 0 36 36" style="position:absolute;top:0;left:0;" class="collar-oob-pulse-svg">` +
                      `<circle cx="18" cy="18" r="16" fill="none" stroke="#FF0000" stroke-width="2" opacity="0.6" class="collar-oob-ring"/></svg>` +
                      iconHtml + `</div>`,
                iconSize: [36, 36],
                iconAnchor: [18, 18],
                className: 'collar-marker-icon collar-oob-active'
            });
        }
        return L.divIcon({
            html: iconHtml,
            iconSize: [24, 24],
            iconAnchor: [12, 12],
            className: 'collar-marker-icon'
        });
    },

    // Abgeschlossene Tracks (Snapshots) pro snapshotId
    _completedTracks: {},

    // Farben für verschiedene Halsbänder
    _colors: [
        '#E63946', '#0077B6', '#2DC653', '#FF851B', '#9B5DE5',
        '#00B4D8', '#F72585', '#FFD166', '#06D6A0', '#FF6B35',
        '#8B4513', '#20B2AA', '#DC143C', '#4169E1', '#228B22',
        '#FF1493', '#DAA520', '#6A0DAD', '#40E0D0', '#CD5C5C'
    ],
    _colorIndex: 0,

    // SignalR-Connection-Referenz
    _connection: null,
    _dotNetRef: null,

    // Initialisiert das Collar-Tracking auf einer bestehenden Karte
    initialize: function (mapId, dotNetReference) {
        this._dotNetRef = dotNetReference;

        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData) {
            console.error('CollarTracking: Karte nicht gefunden:', mapId);
            return false;
        }

        // Alte Tracks zurücksetzen (wichtig bei Seitennavigation, da die alte Karte disposed wird)
        this._tracks = {};
        this._completedTracks = {};
        this._colorIndex = 0;

        // Eigene FeatureGroup für Tracking-Layer
        if (!mapData.trackingLayer) {
            mapData.trackingLayer = new L.FeatureGroup();
            mapData.map.addLayer(mapData.trackingLayer);
        }

        console.log('CollarTracking: Initialisiert auf Karte', mapId);
        return true;
    },

    // Neue GPS-Position empfangen und auf der Karte zeichnen
    // color ist optional – wenn angegeben, wird diese Farbe verwendet/aktualisiert
    // dogLabel ist optional – wird im Popup angezeigt ("Hund (Team)")
    updatePosition: function (mapId, collarId, lat, lng, timestamp, color, dogLabel) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        // Track initialisieren falls neu
        if (!this._tracks[collarId]) {
            this._tracks[collarId] = {
                positions: [],
                polyline: null,
                outlineOverlay: null,
                dotOverlay: null,
                marker: null,
                baseColor: color || this._colors[this._colorIndex % this._colors.length]
            };
            if (!color) this._colorIndex++;
        }

        // Farbe aktualisieren falls explizit übergeben und geändert
        if (color && this._tracks[collarId].baseColor !== color) {
            this._setTrackColors(mapData, collarId, color);
        }
        // Label aktualisieren falls mitgeliefert
        if (dogLabel) this._tracks[collarId].dogLabel = dogLabel;

        const track = this._tracks[collarId];
        const trackColor = this._resolveTrackColor(track.baseColor);
        const markerColor = this._resolveColor(track.baseColor, this._markerColorMode);
        track.positions.push([lat, lng]);

        // Polyline aktualisieren oder erstellen
        if (track.polyline && mapData.trackingLayer.hasLayer(track.polyline)) {
            track.polyline.addLatLng([lat, lng]);
            if (track.outlineOverlay) track.outlineOverlay.addLatLng([lat, lng]);
            if (track.dotOverlay) track.dotOverlay.addLatLng([lat, lng]);
        } else {
            // Polyline neu erstellen (auch wenn alte Referenz existiert aber nicht mehr auf der Karte ist)
            track.outlineOverlay = this._createTrackOutline(track.positions, 0.9);
            if (track.outlineOverlay) track.outlineOverlay.addTo(mapData.trackingLayer);
            track.polyline = L.polyline(track.positions, {
                color: trackColor,
                weight: 3,
                opacity: 0.8,
                dashArray: null
            });
            track.polyline.addTo(mapData.trackingLayer);
            track.dotOverlay = this._createTrackDotOverlay(track.positions, track.baseColor, 0.8);
            if (track.dotOverlay) track.dotOverlay.addTo(mapData.trackingLayer);
        }

        // Aktuellen Positions-Marker aktualisieren
        if (track.marker && mapData.trackingLayer.hasLayer(track.marker)) {
            track.marker.setLatLng([lat, lng]);
        } else {
            const icon = this._createCollarIcon(markerColor, track._oobActive);
            track.marker = L.marker([lat, lng], { icon: icon })
                .bindPopup(`<strong>${track.dogLabel || collarId}</strong><br><small>Halsband: ${collarId}</small><br><small>UTM: ${latLngToUtm(lat, lng)}</small><br><small>${new Date(timestamp).toLocaleTimeString('de-DE')}</small>`);
            track.marker.addTo(mapData.trackingLayer);
        }

        // Popup aktualisieren
        track.marker.setPopupContent(
            `<strong>${track.dogLabel || collarId}</strong><br>` +
            `<small>Halsband: ${collarId}</small><br>` +
            `<small>UTM: ${latLngToUtm(lat, lng)}</small><br>` +
            `<small>Lat: ${lat.toFixed(5)}, Lng: ${lng.toFixed(5)}</small><br>` +
            `<small>${new Date(timestamp).toLocaleTimeString('de-DE')}</small>`
        );
    },

    // Bestehenden Pfadverlauf eines Halsbands laden und zeichnen
    // color ist optional – wenn angegeben, wird diese Farbe verwendet
    loadHistory: function (mapId, collarId, locations, color, dogLabel) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer || !locations || locations.length === 0) return;

        // Track zurücksetzen
        this.clearTrack(mapId, collarId);

        // Initialisieren
        if (!this._tracks[collarId]) {
            this._tracks[collarId] = {
                positions: [],
                polyline: null,
                outlineOverlay: null,
                dotOverlay: null,
                marker: null,
                baseColor: color || this._colors[this._colorIndex % this._colors.length],
                dogLabel: dogLabel || collarId
            };
            if (!color) this._colorIndex++;
        } else if (color) {
            this._tracks[collarId].baseColor = color;
        }
        if (dogLabel) this._tracks[collarId].dogLabel = dogLabel;

        const track = this._tracks[collarId];
        const trackColor = this._resolveTrackColor(track.baseColor);
        const markerColor = this._resolveColor(track.baseColor, this._markerColorMode);
        const positions = locations.map(loc => [loc.latitude, loc.longitude]);
        track.positions = positions;

        if (positions.length > 0) {
            // Polyline zeichnen
            track.outlineOverlay = this._createTrackOutline(positions, 0.9);
            if (track.outlineOverlay) track.outlineOverlay.addTo(mapData.trackingLayer);
            track.polyline = L.polyline(positions, {
                color: trackColor,
                weight: 3,
                opacity: 0.8
            });
            track.polyline.addTo(mapData.trackingLayer);
            track.dotOverlay = this._createTrackDotOverlay(positions, track.baseColor, 0.8);
            if (track.dotOverlay) track.dotOverlay.addTo(mapData.trackingLayer);

            // Marker an letzter Position
            const lastPos = positions[positions.length - 1];
            const lastLoc = locations[locations.length - 1];
            const icon = this._createCollarIcon(markerColor, false);
            track.marker = L.marker(lastPos, { icon: icon })
                .bindPopup(`<strong>${track.dogLabel || collarId}</strong><br><small>Halsband: ${collarId}</small><br><small>UTM: ${latLngToUtm(lastPos[0], lastPos[1])}</small><br><small>${new Date(lastLoc.timestamp).toLocaleTimeString('de-DE')}</small>`);
            track.marker.addTo(mapData.trackingLayer);
        }
    },

    // Out-of-Bounds Warnung anzeigen: pulsierender Ring am Track-Kopf
    showOutOfBoundsWarning: function (mapId, collarId, teamId, lat, lng) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData) return;

        const track = this._tracks[collarId];
        if (!track || !track.marker) return;

        // Marker-Icon durch pulsierende Variante ersetzen
        const icon = this._createCollarIcon(this._resolveColor(track.baseColor, this._markerColorMode), true);
        track.marker.setIcon(icon);
        track._oobActive = true;

        // Warnung nach 15 Sekunden zurücknehmen (normales Icon wiederherstellen)
        if (track._oobTimeout) clearTimeout(track._oobTimeout);
        track._oobTimeout = setTimeout(() => {
            this._clearOobWarning(mapData, collarId);
        }, 15000);
    },

    // Out-of-Bounds-Puls vom Marker entfernen (intern)
    _clearOobWarning: function (mapData, collarId) {
        const track = this._tracks[collarId];
        if (!track || !track.marker || !track._oobActive) return;

        if (track._oobTimeout) {
            clearTimeout(track._oobTimeout);
            track._oobTimeout = null;
        }

        const icon = this._createCollarIcon(this._resolveColor(track.baseColor, this._markerColorMode), false);
        track.marker.setIcon(icon);
        track._oobActive = false;
    },

    // Out-of-Bounds-Puls vom Marker entfernen (von Blazor aufrufbar)
    clearOobWarning: function (mapId, collarId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData) return;
        this._clearOobWarning(mapData, collarId);
    },

    // Track eines einzelnen Halsbands löschen
    clearTrack: function (mapId, collarId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        const track = this._tracks[collarId];
        if (track) {
            if (track.polyline) mapData.trackingLayer.removeLayer(track.polyline);
            if (track.outlineOverlay) mapData.trackingLayer.removeLayer(track.outlineOverlay);
            if (track.dotOverlay) mapData.trackingLayer.removeLayer(track.dotOverlay);
            if (track.marker) mapData.trackingLayer.removeLayer(track.marker);
            delete this._tracks[collarId];
        }
    },

    // Alle Tracks löschen
    clearAll: function (mapId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        mapData.trackingLayer.clearLayers();
        this._tracks = {};
        this._colorIndex = 0;
    },

    // Farbe eines bestehenden Tracks ändern (Polyline + Marker neu zeichnen)
    _setTrackColors: function (mapData, collarId, newColor) {
        const track = this._tracks[collarId];
        if (!track) return;
        track.baseColor = newColor;
        const trackColor = this._resolveTrackColor(newColor);
        const markerColor = this._resolveColor(newColor, this._markerColorMode);

        // Polyline-Farbe aktualisieren
        if (track.polyline && mapData.trackingLayer.hasLayer(track.polyline)) {
            track.polyline.setStyle({ color: trackColor });
        }
        if (track.outlineOverlay && mapData.trackingLayer.hasLayer(track.outlineOverlay)) {
            track.outlineOverlay.setStyle({ color: this._trackColorMode === 'area-cased' ? '#000000' : '#ffffff' });
        }
        if (track.dotOverlay && mapData.trackingLayer.hasLayer(track.dotOverlay)) {
            track.dotOverlay.setStyle({ color: this._resolveColor(newColor, 'area') });
        }

        // Marker-Icon mit neuer Farbe ersetzen
        if (track.marker && mapData.trackingLayer.hasLayer(track.marker)) {
            const icon = this._createCollarIcon(markerColor, track._oobActive);
            track.marker.setIcon(icon);
        }
    },

    // Tracking-Sichtbarkeit umschalten
    toggleVisibility: function (mapId, visible) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        if (visible) {
            if (!mapData.map.hasLayer(mapData.trackingLayer)) {
                mapData.map.addLayer(mapData.trackingLayer);
            }
        } else {
            if (mapData.map.hasLayer(mapData.trackingLayer)) {
                mapData.map.removeLayer(mapData.trackingLayer);
            }
        }
    },

    // Abgeschlossenen Track (Snapshot) auf der Karte einzeichnen (gedimmt, gestrichelt)
    // trackType: "CollarTrack" (Standard) oder "HumanTrack" (Mensch-Laufweg, anders dargestellt)
    addCompletedTrack: function (mapId, snapshotId, points, color, teamName, collarName, trackType) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;
        if (!points || points.length === 0) return;

        const isHumanTrack = trackType === 'HumanTrack';

        // Bereits vorhandenen Eintrag entfernen
        this._removeCompletedTrackLayers(mapData, snapshotId);

        const positions = points.map(p => [p.latitude, p.longitude]);

        const outlineOverlay = this._createTrackOutline(positions, isHumanTrack ? 0.7 : 0.6);
        if (outlineOverlay) outlineOverlay.addTo(mapData.trackingLayer);
        const polyline = L.polyline(positions, {
            color: this._resolveTrackColor(color),
            weight: isHumanTrack ? 3 : 4,
            opacity: isHumanTrack ? 0.7 : 0.5,
            dashArray: isHumanTrack ? '3 8' : '6 5'
        });
        polyline.addTo(mapData.trackingLayer);
        const dotOverlay = isHumanTrack ? null : this._createTrackDotOverlay(positions, color, 0.5);
        if (dotOverlay) dotOverlay.addTo(mapData.trackingLayer);

        // Klick auf Linie → Blazor-Callback um GPS-Tab zu öffnen und Snapshot aufzuklappen
        polyline.on('click', () => {
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnCompletedTrackClicked', snapshotId);
            }
        });
        polyline.on('mouseover', function () { this.setStyle({ weight: isHumanTrack ? 5 : 6, opacity: 0.8 }); });
        polyline.on('mouseout', function () { this.setStyle({ weight: isHumanTrack ? 3 : 4, opacity: isHumanTrack ? 0.7 : 0.5 }); });

        const trackLabel = isHumanTrack ? 'Laufweg' : collarName;

        // Start-Marker (kleines Dreieck)
        const startIcon = L.divIcon({
            html: `<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 14 14">
                <polygon points="7,1 13,13 1,13" fill="${this._resolveColor(color, this._trackColorMode)}" stroke="white" stroke-width="1.5" opacity="0.7"/>
            </svg>`,
            iconSize: [14, 14], iconAnchor: [7, 7], className: 'collar-completed-icon'
        });
        const startMarker = L.marker(positions[0], { icon: startIcon })
            .bindPopup(`<strong>${teamName}</strong><br><small>Start · ${trackLabel}</small>`);
        startMarker.addTo(mapData.trackingLayer);

        // End-Marker (kleines Quadrat)
        const lastPos = positions[positions.length - 1];
        const endIcon = L.divIcon({
            html: `<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 14 14">
                <rect x="1" y="1" width="12" height="12" fill="${this._resolveColor(color, this._trackColorMode)}" stroke="white" stroke-width="1.5" opacity="0.7"/>
            </svg>`,
            iconSize: [14, 14], iconAnchor: [7, 7], className: 'collar-completed-icon'
        });
        const endMarker = L.marker(lastPos, { icon: endIcon })
            .bindPopup(`<strong>${teamName}</strong><br><small>Ende · ${trackLabel}</small>`);
        endMarker.addTo(mapData.trackingLayer);

        this._completedTracks[snapshotId] = {
            polyline: polyline,
            outlineOverlay: outlineOverlay,
            dotOverlay: dotOverlay,
            startMarker: startMarker,
            endMarker: endMarker,
            visible: true
        };
    },

    // Sichtbarkeit eines abgeschlossenen Tracks umschalten
    toggleCompletedTrack: function (mapId, snapshotId, visible) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        const ct = this._completedTracks[snapshotId];
        if (!ct) return;

        ct.visible = visible;
        [ct.outlineOverlay, ct.polyline, ct.dotOverlay, ct.startMarker, ct.endMarker].forEach(layer => {
            if (!layer) return;
            if (visible) {
                if (!mapData.trackingLayer.hasLayer(layer)) mapData.trackingLayer.addLayer(layer);
            } else {
                if (mapData.trackingLayer.hasLayer(layer)) mapData.trackingLayer.removeLayer(layer);
            }
        });
    },

    // Alle abgeschlossenen Tracks entfernen
    clearAllCompletedTracks: function (mapId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        for (const id in this._completedTracks) {
            this._removeCompletedTrackLayers(mapData, id);
        }
        this._completedTracks = {};
    },

    // Intern: Layer eines abgeschlossenen Tracks von der Karte entfernen
    _removeCompletedTrackLayers: function (mapData, snapshotId) {
        const ct = this._completedTracks[snapshotId];
        if (!ct) return;
        [ct.outlineOverlay, ct.polyline, ct.dotOverlay, ct.startMarker, ct.endMarker].forEach(layer => {
            if (layer && mapData.trackingLayer.hasLayer(layer))
                mapData.trackingLayer.removeLayer(layer);
        });
        delete this._completedTracks[snapshotId];
    },

    // Zentriert und zoomt die Karte so, dass der gesamte Live-Track eines Halsbands sichtbar ist
    zoomToCollar: function (mapId, collarId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData) return false;

        const track = this._tracks[collarId];
        if (!track) return false;

        if (track.positions && track.positions.length > 1) {
            // Gesamten Pfad in den Viewport einpassen
            const bounds = L.latLngBounds(track.positions);
            mapData.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 18 });
            if (track.marker) track.marker.openPopup();
            return true;
        }

        // Fallback: nur aktuelle Position
        const pos = track.marker ? track.marker.getLatLng()
            : (track.positions && track.positions.length === 1 ? track.positions[0] : null);
        if (pos) {
            mapData.map.setView(pos, Math.max(mapData.map.getZoom(), 17));
            if (track.marker) track.marker.openPopup();
            return true;
        }

        return false;
    },

    // Zoomt auf einen abgeschlossenen Track (Snapshot) und passt die Bounds an
    zoomToCompletedTrack: function (mapId, snapshotId) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData) return;

        const ct = this._completedTracks[snapshotId];
        if (!ct || !ct.polyline) return;

        const bounds = ct.polyline.getBounds();
        if (bounds.isValid()) {
            mapData.map.fitBounds(bounds, { padding: [50, 50], maxZoom: 18 });
        }
    }
};
