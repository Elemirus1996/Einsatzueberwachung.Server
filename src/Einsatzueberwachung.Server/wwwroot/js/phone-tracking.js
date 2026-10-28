// Handy-GPS-Tracking: Zeigt Team-Telefon-Positionen auf der Karte

window.PhoneTracking = (function () {
    // teamId -> { marker }
    const _markers = {};

    // teamId -> { polyline, color }
    const _tracks = {};

    // Einzeln ausgeblendete Handys (Auge am Eintrag im Handy-Tab) – Marker und Laufweg bleiben erhalten, nur unsichtbar
    const _hidden = new Set();

    function _show(mapData, teamId, layer) {
        if (!_hidden.has(teamId)) layer.addTo(mapData.phoneLayer);
    }

    // Konfiguriertes Symbol ("phone" | "person" | "person_walking" | "radio" | "dot")
    let _humanIcon = 'phone';

    function _getHumanIconClass() {
        switch (_humanIcon) {
            case 'person':         return 'bi-person-fill';
            case 'person_walking': return 'bi-person-walking';
            case 'radio':          return 'bi-broadcast';
            case 'dot':            return 'bi-circle-fill';
            default:               return 'bi-phone-fill'; // phone
        }
    }

    function setOptions(opts) {
        if (opts && opts.humanIcon) _humanIcon = opts.humanIcon;
    }

    function _getMapData(mapId) {
        return window.LeafletMap?.maps?.[mapId] ?? null;
    }

    function initialize(mapId) {
        const mapData = _getMapData(mapId);
        if (!mapData) return;

        if (!mapData.phoneLayer) {
            mapData.phoneLayer = new L.FeatureGroup();
            mapData.map.addLayer(mapData.phoneLayer);
        }
        // Zustand aus altem Seitenbesuch zurücksetzen
        _hidden.clear();
        Object.keys(_markers).forEach(k => delete _markers[k]);
        Object.keys(_elMarkers).forEach(k => delete _elMarkers[k]);
        Object.keys(_tracks).forEach(k => delete _tracks[k]);
    }

    function _createHumanIcon(color) {
        const iconClass = _getHumanIconClass();
        return L.divIcon({
            className: '',
            html: `<i class="bi ${iconClass}" style="font-size:24px;color:${color};filter:drop-shadow(0 1px 3px rgba(0,0,0,0.55));display:block;line-height:1;"></i>`,
            iconSize: [24, 24],
            iconAnchor: [12, 12],
            popupAnchor: [0, -14]
        });
    }

    function _buildPopup(teamName, lat, lng, timestamp) {
        const time = timestamp
            ? new Date(timestamp).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
            : '--:--';
        return `<strong>&#128241; ${escapeHtml(teamName)}</strong><br>` +
               `<small>Lat: ${lat.toFixed(5)}, Lng: ${lng.toFixed(5)}</small><br>` +
               `<small>Stand: ${time}</small>`;
    }

    function _teamColor(teamId) {
        const colors = ['#1976d2', '#d32f2f', '#388e3c', '#f57c00', '#7b1fa2',
                        '#00838f', '#c2185b', '#5d4037', '#455a64', '#0288d1'];
        let h = 0;
        for (let i = 0; i < teamId.length; i++) h = (h * 31 + teamId.charCodeAt(i)) & 0xffff;
        return colors[h % colors.length];
    }

    function updateMarker(mapId, teamId, teamName, lat, lng, timestamp) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;

        const color = _teamColor(teamId);

        const popupContent = _buildPopup(teamName, lat, lng, timestamp);

        if (_markers[teamId]) {
            _markers[teamId].setLatLng([lat, lng]);
            _markers[teamId].setPopupContent(popupContent);
        } else {
            const icon = _createHumanIcon(color);
            const marker = L.marker([lat, lng], { icon })
                .bindPopup(popupContent);
            _show(mapData, teamId, marker);
            _markers[teamId] = marker;
        }
    }

    /**
     * Lädt einen bestehenden Telefon-GPS-Track und zeichnet ihn als Polylinie.
     * @param {string} mapId
     * @param {string} teamId
     * @param {{lat: number, lng: number}[]} points
     * @param {string} [color]
     */
    function loadTrack(mapId, teamId, points, color) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;
        if (!points || points.length < 2) return;

        const trackColor = color || _teamColor(teamId);

        // Bestehenden Track entfernen falls vorhanden
        if (_tracks[teamId]) {
            mapData.phoneLayer.removeLayer(_tracks[teamId].polyline);
        }

        const polyline = L.polyline(
            points.map(p => [p.lat, p.lng]),
            { color: trackColor, weight: 3, opacity: 0.7, dashArray: '3 8' }
        );
        _show(mapData, teamId, polyline);

        _tracks[teamId] = { polyline, color: trackColor };
    }

    /**
     * Fügt einen einzelnen GPS-Punkt zum laufenden Telefon-Track hinzu.
     * @param {string} mapId
     * @param {string} teamId
     * @param {number} lat
     * @param {number} lng
     */
    function appendTrackPoint(mapId, teamId, lat, lng) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;

        if (!_tracks[teamId]) {
            const trackColor = _teamColor(teamId);
            const polyline = L.polyline([[lat, lng]], {
                color: trackColor, weight: 3, opacity: 0.7, dashArray: '3 8'
            });
            _show(mapData, teamId, polyline);
            _tracks[teamId] = { polyline, color: trackColor };
        } else {
            _tracks[teamId].polyline.addLatLng([lat, lng]);
        }
    }

    /**
     * Entfernt den Telefon-Track eines Teams von der Karte.
     */
    function clearTrack(mapId, teamId) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;
        if (_tracks[teamId]) {
            mapData.phoneLayer.removeLayer(_tracks[teamId].polyline);
            delete _tracks[teamId];
        }
    }

    function toggleVisibility(mapId, visible) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;

        if (visible) {
            if (!mapData.map.hasLayer(mapData.phoneLayer)) {
                mapData.map.addLayer(mapData.phoneLayer);
            }
        } else {
            mapData.map.removeLayer(mapData.phoneLayer);
        }
    }

    /** Ein einzelnes Handy (Marker und Laufweg) aus- oder einblenden. */
    function setTeamVisible(mapId, teamId, visible) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;
        const teile = [_markers[teamId], _tracks[teamId] && _tracks[teamId].polyline].filter(Boolean);
        if (visible) {
            _hidden.delete(teamId);
            teile.forEach(l => { if (!mapData.phoneLayer.hasLayer(l)) l.addTo(mapData.phoneLayer); });
        } else {
            _hidden.add(teamId);
            teile.forEach(l => mapData.phoneLayer.removeLayer(l));
        }
    }

    function removeMarker(mapId, teamId) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;
        if (_markers[teamId]) {
            mapData.phoneLayer.removeLayer(_markers[teamId]);
            delete _markers[teamId];
        }
    }

    function clearAll(mapId) {
        const mapData = _getMapData(mapId);
        if (!mapData || !mapData.phoneLayer) return;
        mapData.phoneLayer.clearLayers();
        Object.keys(_markers).forEach(k => delete _markers[k]);
        Object.keys(_tracks).forEach(k => delete _tracks[k]);
    }

    /**
     * Zoomt die Karte auf den aktuellen Marker eines Teams und öffnet dessen Popup.
     * @param {string} mapId
     * @param {string} teamId
     */
    function zoomToTeam(mapId, teamId) {
        const mapData = _getMapData(mapId);
        if (!mapData) return;

        const marker = _markers[teamId];
        if (!marker) return;

        mapData.map.setView(marker.getLatLng(), 16);
        marker.openPopup();
    }

    // ── Einsatzleitung (Tracking-App, #110): eigener, immer sichtbarer Layer ──────────────────
    // geraet -> marker
    const _elMarkers = {};

    function _elLayer(mapData) {
        if (!mapData.elLayer) {
            mapData.elLayer = new L.FeatureGroup();
            mapData.map.addLayer(mapData.elLayer);
        }
        return mapData.elLayer;
    }

    function setElPosition(mapId, geraet, lat, lng, timestamp, akku) {
        const mapData = _getMapData(mapId);
        if (!mapData) return;
        const time = timestamp
            ? new Date(timestamp).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
            : '--:--';
        const popup = `<strong>Einsatzleitung · ${escapeHtml(geraet)}</strong><br>` +
                      `<small>Stand: ${time}${akku != null ? ' · Akku ' + akku + ' %' : ''}</small>`;
        if (_elMarkers[geraet]) {
            _elMarkers[geraet].setLatLng([lat, lng]);
            _elMarkers[geraet].setPopupContent(popup);
            return;
        }
        const icon = L.divIcon({
            className: '',
            html: '<div class="eu-el-marker" title="Einsatzleitung">EL</div>',
            iconSize: [30, 22],
            iconAnchor: [15, 11],
            popupAnchor: [0, -12]
        });
        const marker = L.marker([lat, lng], { icon, zIndexOffset: 900 })
            .bindPopup(popup)
            .bindTooltip(escapeHtml(geraet), { direction: 'bottom', offset: [0, 10] });
        marker.addTo(_elLayer(mapData));
        _elMarkers[geraet] = marker;
    }

    function clearElPositions(mapId) {
        const mapData = _getMapData(mapId);
        if (mapData && mapData.elLayer) mapData.elLayer.clearLayers();
        Object.keys(_elMarkers).forEach(k => delete _elMarkers[k]);
    }

    return { initialize, updateMarker, loadTrack, appendTrackPoint, clearTrack, toggleVisibility, removeMarker, clearAll, setOptions, zoomToTeam,
        setTeamVisible,
        setElPosition, clearElPositions };
})();
