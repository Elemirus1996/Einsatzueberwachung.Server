// Live-Tracking von GPS-Halsbändern auf der Leaflet-Karte
// Zeichnet Polylines (Pfade) für zugewiesene Halsbänder und zeigt Warnungen bei Out-of-Bounds

window.CollarTracking = {
    // Gespeicherte Tracking-Daten pro Halsband
    _tracks: {},

    // Farben für verschiedene Halsbänder
    _colors: [
        '#FF4444', '#44FF44', '#4444FF', '#FFAA00', '#FF44FF',
        '#00FFFF', '#FF8800', '#8844FF', '#44FF88', '#FF4488',
        '#88FF44', '#4488FF', '#FFFF44', '#FF0088', '#00FF88',
        '#8800FF', '#FF8844', '#4488FF', '#88FF00', '#0044FF'
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
    updatePosition: function (mapId, collarId, lat, lng, timestamp, color) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer) return;

        // Track initialisieren falls neu
        if (!this._tracks[collarId]) {
            this._tracks[collarId] = {
                positions: [],
                polyline: null,
                marker: null,
                color: color || this._colors[this._colorIndex % this._colors.length]
            };
            if (!color) this._colorIndex++;
        }

        // Farbe aktualisieren falls explizit übergeben und geändert
        if (color && this._tracks[collarId].color !== color) {
            this._setTrackColor(mapData, collarId, color);
        }

        const track = this._tracks[collarId];
        track.positions.push([lat, lng]);

        // Polyline aktualisieren oder erstellen
        if (track.polyline && mapData.trackingLayer.hasLayer(track.polyline)) {
            track.polyline.addLatLng([lat, lng]);
        } else {
            // Polyline neu erstellen (auch wenn alte Referenz existiert aber nicht mehr auf der Karte ist)
            track.polyline = L.polyline(track.positions, {
                color: track.color,
                weight: 3,
                opacity: 0.8,
                dashArray: null
            });
            track.polyline.addTo(mapData.trackingLayer);
        }

        // Aktuellen Positions-Marker aktualisieren
        if (track.marker && mapData.trackingLayer.hasLayer(track.marker)) {
            track.marker.setLatLng([lat, lng]);
        } else {
            const size = track._oobActive ? 36 : 20;
            const half = size / 2;
            const oobRing = track._oobActive
                ? `<circle cx="${half}" cy="${half}" r="${half - 2}" fill="none" stroke="#FF0000" stroke-width="2" opacity="0.6" class="collar-oob-ring"/>`
                : '';
            const icon = L.divIcon({
                html: `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
                    ${oobRing}
                    <circle cx="${half}" cy="${half}" r="8" fill="${track.color}" stroke="white" stroke-width="2"/>
                    <circle cx="${half}" cy="${half}" r="3" fill="white"/>
                </svg>`,
                iconSize: [size, size],
                iconAnchor: [half, half],
                className: track._oobActive ? 'collar-marker-icon collar-oob-active' : 'collar-marker-icon'
            });
            track.marker = L.marker([lat, lng], { icon: icon })
                .bindPopup(`<strong>Halsband: ${collarId}</strong><br><small>${new Date(timestamp).toLocaleTimeString('de-DE')}</small>`);
            track.marker.addTo(mapData.trackingLayer);
        }

        // Popup aktualisieren
        track.marker.setPopupContent(
            `<strong>Halsband: ${collarId}</strong><br>` +
            `<small>Lat: ${lat.toFixed(5)}, Lng: ${lng.toFixed(5)}</small><br>` +
            `<small>${new Date(timestamp).toLocaleTimeString('de-DE')}</small>`
        );
    },

    // Bestehenden Pfadverlauf eines Halsbands laden und zeichnen
    // color ist optional – wenn angegeben, wird diese Farbe verwendet
    loadHistory: function (mapId, collarId, locations, color) {
        const mapData = window.LeafletMap.maps[mapId];
        if (!mapData || !mapData.trackingLayer || !locations || locations.length === 0) return;

        // Track zurücksetzen
        this.clearTrack(mapId, collarId);

        // Initialisieren
        if (!this._tracks[collarId]) {
            this._tracks[collarId] = {
                positions: [],
                polyline: null,
                marker: null,
                color: color || this._colors[this._colorIndex % this._colors.length]
            };
            if (!color) this._colorIndex++;
        } else if (color) {
            this._tracks[collarId].color = color;
        }

        const track = this._tracks[collarId];
        const positions = locations.map(loc => [loc.latitude, loc.longitude]);
        track.positions = positions;

        if (positions.length > 0) {
            // Polyline zeichnen
            track.polyline = L.polyline(positions, {
                color: track.color,
                weight: 3,
                opacity: 0.8
            });
            track.polyline.addTo(mapData.trackingLayer);

            // Marker an letzter Position
            const lastPos = positions[positions.length - 1];
            const lastLoc = locations[locations.length - 1];
            const icon = L.divIcon({
                html: `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">
                    <circle cx="10" cy="10" r="8" fill="${track.color}" stroke="white" stroke-width="2"/>
                    <circle cx="10" cy="10" r="3" fill="white"/>
                </svg>`,
                iconSize: [20, 20],
                iconAnchor: [10, 10],
                className: 'collar-marker-icon'
            });
            track.marker = L.marker(lastPos, { icon: icon })
                .bindPopup(`<strong>Halsband: ${collarId}</strong><br><small>${new Date(lastLoc.timestamp).toLocaleTimeString('de-DE')}</small>`);
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
        const icon = L.divIcon({
            html: `<svg xmlns="http://www.w3.org/2000/svg" width="36" height="36" viewBox="0 0 36 36" class="collar-oob-pulse-svg">
                <circle cx="18" cy="18" r="16" fill="none" stroke="#FF0000" stroke-width="2" opacity="0.6" class="collar-oob-ring"/>
                <circle cx="18" cy="18" r="8" fill="${track.color}" stroke="white" stroke-width="2"/>
                <circle cx="18" cy="18" r="3" fill="white"/>
            </svg>`,
            iconSize: [36, 36],
            iconAnchor: [18, 18],
            className: 'collar-marker-icon collar-oob-active'
        });
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

        const icon = L.divIcon({
            html: `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">
                <circle cx="10" cy="10" r="8" fill="${track.color}" stroke="white" stroke-width="2"/>
                <circle cx="10" cy="10" r="3" fill="white"/>
            </svg>`,
            iconSize: [20, 20],
            iconAnchor: [10, 10],
            className: 'collar-marker-icon'
        });
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
    _setTrackColor: function (mapData, collarId, newColor) {
        const track = this._tracks[collarId];
        if (!track) return;
        track.color = newColor;

        // Polyline-Farbe aktualisieren
        if (track.polyline && mapData.trackingLayer.hasLayer(track.polyline)) {
            track.polyline.setStyle({ color: newColor });
        }

        // Marker-Icon mit neuer Farbe ersetzen
        if (track.marker && mapData.trackingLayer.hasLayer(track.marker)) {
            const icon = L.divIcon({
                html: `<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">
                    <circle cx="10" cy="10" r="8" fill="${newColor}" stroke="white" stroke-width="2"/>
                    <circle cx="10" cy="10" r="3" fill="white"/>
                </svg>`,
                iconSize: [20, 20],
                iconAnchor: [10, 10],
                className: 'collar-marker-icon'
            });
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
    }
};
