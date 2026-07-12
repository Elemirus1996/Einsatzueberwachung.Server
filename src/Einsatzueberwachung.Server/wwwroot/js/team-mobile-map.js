window.teamMobileMap = (function () {
    let map = null;
    let polygonLayer = null;
    let dogMarker = null;
    let trackLine = null;
    let trackOutline = null;
    let trackDots = null;
    let userMarker = null;
    let userTrackLine = null;
    // Abgeschlossene Track-Episoden früherer Suchläufe (historisch, persistent auf dem Server)
    let historicalTracks = [];

    // Konfigurierbare Marker-Symbole
    let _collarIcon = 'paw';
    let _humanIcon = 'phone';

    // Farbe des aktuellen Suchgebiets – wird von renderSearchArea gesetzt
    let _areaColor = '#dc3545';
    let _trackColorMode = 'black';
    let _markerColorMode = 'area-black-outline';

    function setOptions(opts) {
        if (opts && opts.collarIcon) _collarIcon = opts.collarIcon;
        if (opts && opts.humanIcon)  _humanIcon  = opts.humanIcon;
        if (opts && ['area', 'black', 'contrast', 'area-dots', 'area-cased', 'black-cased'].includes(opts.trackColorMode)) _trackColorMode = opts.trackColorMode;
        if (opts && ['area', 'black', 'contrast', 'area-black-outline', 'area-white-outline', 'black-white-outline'].includes(opts.markerColorMode)) _markerColorMode = opts.markerColorMode;
    }

    function _resolveColor(mode) {
        if (mode === 'black' || mode === 'black-white-outline') return '#000000';
        if (mode !== 'contrast' || !/^#[0-9a-f]{6}$/i.test(_areaColor)) return _areaColor;
        const red = parseInt(_areaColor.slice(1, 3), 16);
        const green = parseInt(_areaColor.slice(3, 5), 16);
        const blue = parseInt(_areaColor.slice(5, 7), 16);
        return `#${(255 - red).toString(16).padStart(2, '0')}${(255 - green).toString(16).padStart(2, '0')}${(255 - blue).toString(16).padStart(2, '0')}`;
    }

    function _resolveTrackColor() {
        return _trackColorMode === 'area-dots' || _trackColorMode === 'black-cased' ? '#000000' : _resolveColor(_trackColorMode);
    }

    function _createTrackOutline(points, opacity) {
        if (_trackColorMode !== 'area-cased' && _trackColorMode !== 'black-cased') return null;

        return L.polyline(points, {
            color: _trackColorMode === 'area-cased' ? '#000000' : '#ffffff',
            weight: 7,
            opacity,
            lineCap: 'round',
            lineJoin: 'round',
            interactive: false
        }).addTo(map);
    }

    function _createTrackDots(points, opacity) {
        if (_trackColorMode !== 'area-dots') return null;

        return L.polyline(points, {
            color: _areaColor,
            weight: 6,
            opacity: opacity,
            dashArray: '1 17',
            lineCap: 'round',
            interactive: false
        }).addTo(map);
    }

    function _getCollarIconClass() {
        switch (_collarIcon) {
            case 'dog':  return 'fa-dog';
            case 'bone': return 'fa-bone';
            case 'crosshairs': return 'fa-crosshairs';
            case 'location-arrow': return 'fa-location-arrow';
            case 'dot':  return 'fa-location-dot';
            default:     return 'fa-paw'; // paw
        }
    }

    function _getCollarIconStyle() {
        const outline = _markerColorMode === 'area-black-outline'
            ? '-webkit-text-stroke:1.5px #000;paint-order:stroke fill;'
            : _markerColorMode === 'area-white-outline' || _markerColorMode === 'black-white-outline'
                ? '-webkit-text-stroke:1.5px #fff;paint-order:stroke fill;'
                : '';
        return `font-size:26px;color:${_resolveColor(_markerColorMode)};${outline}filter:drop-shadow(0 1px 3px rgba(0,0,0,0.55));display:block;line-height:1;`;
    }

    function _getHumanIconClass() {
        switch (_humanIcon) {
            case 'person':         return 'bi-person-fill';
            case 'person_walking': return 'bi-person-walking';
            case 'radio':          return 'bi-broadcast';
            case 'dot':            return 'bi-circle-fill';
            default:               return 'bi-phone-fill';
        }
    }

    function init(containerId, fallbackLat, fallbackLng) {
        if (map) return;
        const el = document.getElementById(containerId);
        if (el && el.clientHeight < 100) {
            // Fallback: explizite Hoehe falls Flex-Layout (CSS) noch nicht geladen ist.
            el.style.height = '60vh';
        }
        const center = [fallbackLat || 51.1657, fallbackLng || 10.4515];
        map = L.map(containerId, { zoomControl: true }).setView(center, 13);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; OpenStreetMap',
            maxZoom: 19
        }).addTo(map);
        // Falls der Container danach noch wachst (z.B. Header rendert), Leaflet neu vermessen.
        setTimeout(() => { try { map.invalidateSize(); } catch (e) { /* ignore */ } }, 250);
        setTimeout(() => { try { map.invalidateSize(); } catch (e) { /* ignore */ } }, 1500);
    }

    function renderSearchArea(coords, color) {
        if (!map) return;
        if (polygonLayer) map.removeLayer(polygonLayer);
        if (!coords || coords.length < 3) return;
        _areaColor = color || '#dc3545';
        polygonLayer = L.polygon(coords.map(c => [c.lat, c.lng]), {
            color: _areaColor,
            weight: 3,
            fillOpacity: 0.15
        }).addTo(map);
        try { map.fitBounds(polygonLayer.getBounds(), { padding: [30, 30] }); } catch (e) { /* ignore */ }
    }

    function setDogPosition(lat, lng, dogName) {
        if (!map) return;
        const pos = [lat, lng];
        if (!dogMarker) {
            const icon = L.divIcon({
                className: 'team-mobile-dog-marker',
                html: `<i class="fa-solid ${_getCollarIconClass()}" style="${_getCollarIconStyle()}"></i>`,
                iconSize: [26, 26],
                iconAnchor: [13, 13]
            });
            dogMarker = L.marker(pos, { icon }).addTo(map);
            if (dogName) dogMarker.bindTooltip(dogName, { permanent: false });
        } else {
            dogMarker.setLatLng(pos);
        }
    }

    function setTrack(points) {
        if (!map) return;
        if (trackLine) { map.removeLayer(trackLine); trackLine = null; }
        if (trackOutline) { map.removeLayer(trackOutline); trackOutline = null; }
        if (trackDots) { map.removeLayer(trackDots); trackDots = null; }
        if (!points || points.length < 2) return;
        const trackPoints = points.map(p => [p.lat, p.lng]);
        trackOutline = _createTrackOutline(trackPoints, 0.8);
        trackLine = L.polyline(trackPoints, {
            color: _resolveTrackColor(),
            weight: 3,
            opacity: 0.7
        }).addTo(map);
        trackDots = _createTrackDots(trackPoints, 0.7);
    }

    function appendTrackPoint(lat, lng) {
        if (!map) return;
        if (!trackLine) {
            trackOutline = _createTrackOutline([[lat, lng]], 0.8);
            trackLine = L.polyline([[lat, lng]], { color: _resolveTrackColor(), weight: 3, opacity: 0.7 }).addTo(map);
            trackDots = _createTrackDots([[lat, lng]], 0.7);
        } else {
            trackLine.addLatLng([lat, lng]);
            if (trackOutline) trackOutline.addLatLng([lat, lng]);
            if (trackDots) trackDots.addLatLng([lat, lng]);
        }
    }

    function setUserPosition(lat, lng) {
        if (!map) return;
        const pos = [lat, lng];
        if (!userMarker) {
            const icon = L.divIcon({
                className: 'team-mobile-user-marker',
                html: `<i class="bi ${_getHumanIconClass()}" style="font-size:24px;color:#0d6efd;filter:drop-shadow(0 1px 3px rgba(0,0,0,0.55));display:block;line-height:1;"></i>`,
                iconSize: [24, 24],
                iconAnchor: [12, 12]
            });
            userMarker = L.marker(pos, { icon }).addTo(map);
        } else {
            userMarker.setLatLng(pos);
        }
    }

    function appendUserTrackPoint(lat, lng) {
        if (!map) return;
        if (!userTrackLine) {
            userTrackLine = L.polyline([[lat, lng]], {
                color: '#0d6efd',
                weight: 3,
                opacity: 0.7,
                dashArray: '3 8'
            }).addTo(map);
        } else {
            userTrackLine.addLatLng([lat, lng]);
        }
    }

    /**
     * Zeichnet einen abgeschlossenen Track einer früheren Suchepisode auf der Karte.
     * Wird beim Seitenaufbau für alle gespeicherten CompletedSearch-Episoden aufgerufen.
     * @param {{lat: number, lng: number}[]} points - Track-Punkte
     * @param {string} color - Farbe der Polylinie
     * @param {boolean} isHumanTrack - true = Mensch-Laufweg (gestrichelt), false = Halsband-Track (durchgezogen)
     */
    function addCompletedTrack(points, color, isHumanTrack) {
        if (!map || !points || points.length < 2) return;
        const polyline = L.polyline(
            points.map(p => [p.lat, p.lng]),
            {
                color: isHumanTrack ? (color || '#888888') : _resolveTrackColor(),
                weight: 3,
                opacity: 0.55,
                dashArray: isHumanTrack ? '4 9' : null
            }
        ).addTo(map);
        historicalTracks.push(polyline);
        if (!isHumanTrack) {
            const dotOverlay = _createTrackDots(points.map(p => [p.lat, p.lng]), 0.55);
            if (dotOverlay) historicalTracks.push(dotOverlay);
        }
    }

    function loadUserTrack(points) {
        if (!map || !points || points.length < 2) return;
        if (userTrackLine) { map.removeLayer(userTrackLine); userTrackLine = null; }
        userTrackLine = L.polyline(
            points.map(p => [p.lat, p.lng]),
            { color: '#0d6efd', weight: 3, opacity: 0.7, dashArray: '3 8' }
        ).addTo(map);
    }

    /**
     * Lädt den aktuellen Halsband-Track und Telefon-GPS-Track direkt vom Server-API (/api/team-mobile/state).
     * Dieser Ansatz umgeht mögliche Größen- oder Timing-Probleme beim Blazor-Interop und stellt sicher,
     * dass der vollständige Track-Verlauf nach jedem Seiten-Reload sichtbar ist.
     */
    function reloadCurrentTracks() {
        fetch('/api/team-mobile/state', { credentials: 'same-origin' })
            .then(r => r.ok ? r.json() : null)
            .then(data => {
                if (!data) return;
                // Halsband-Track des Hundes
                if (data.track && data.track.length >= 2) {
                    setTrack(data.track);
                }
                if (data.lastLocation) {
                    setDogPosition(data.lastLocation.lat, data.lastLocation.lng, data.team?.dogName || '');
                }
                // Eigener Telefon-GPS-Track (Mensch)
                if (data.phoneTrack && data.phoneTrack.length >= 2) {
                    loadUserTrack(data.phoneTrack);
                }
            })
            .catch(() => {});
    }

    function centerOnDog() {
        if (map && dogMarker) map.panTo(dogMarker.getLatLng());
    }

    function destroy() {
        stopWatchingUser();
        historicalTracks.forEach(t => { try { if (map) map.removeLayer(t); } catch (e) { /* ignore */ } });
        historicalTracks = [];
        if (map) { map.remove(); map = null; }
        polygonLayer = null;
        dogMarker = null;
        trackLine = null;
        trackDots = null;
        userMarker = null;
        userTrackLine = null;
    }

    let watchId = null;
    let dotNetRef = null;
    // Nicht-standardisierter Fehlercode für UI-Rückmeldung:
    // GeolocationPositionError nutzt nur 1..3, wir ergänzen 4 = unsicherer Kontext (kein HTTPS/localhost).
    const GEO_ERROR_INSECURE_CONTEXT = 4;

    function canUseGeolocation(ref) {
        if (!('geolocation' in navigator)) {
            if (ref) ref.invokeMethodAsync('OnUserLocationError', 2).catch(() => {});
            return false;
        }
        if (!window.isSecureContext) {
            if (ref) ref.invokeMethodAsync('OnUserLocationError', GEO_ERROR_INSECURE_CONTEXT).catch(() => {});
            return false;
        }
        return true;
    }

    function startWatchingUser(ref) {
        if (!canUseGeolocation(ref)) return false;
        dotNetRef = ref;
        if (watchId !== null) return true;
        watchId = navigator.geolocation.watchPosition(
            pos => {
                const lat = pos.coords.latitude;
                const lng = pos.coords.longitude;
                setUserPosition(lat, lng);
                appendUserTrackPoint(lat, lng);
                postLocation(lat, lng);
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnUserLocation', lat, lng).catch(() => {});
                }
            },
            err => {
                console.warn('Geolocation error', err);
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnUserLocationError', err.code).catch(() => {});
                }
            },
            { enableHighAccuracy: true, maximumAge: 5000, timeout: 15000 }
        );
        return true;
    }

    function clearWatch() {
        if (watchId !== null) {
            navigator.geolocation.clearWatch(watchId);
            watchId = null;
        }
    }

    /**
     * Fordert explizit die GPS-Berechtigung an, indem zuerst getCurrentPosition() aufgerufen wird
     * (löst den Browser-Berechtigungs-Dialog aus) und startet danach watchPosition() neu.
     * Wird vom Nutzer über den "Erneut versuchen"-Button ausgelöst.
     */
    function requestGeolocationPermission(ref) {
        if (!canUseGeolocation(ref)) return;
        // Bestehenden Watch stoppen, damit ein neuer Dialog ausgelöst werden kann
        clearWatch();
        dotNetRef = ref;
        navigator.geolocation.getCurrentPosition(
            pos => {
                const lat = pos.coords.latitude;
                const lng = pos.coords.longitude;
                setUserPosition(lat, lng);
                appendUserTrackPoint(lat, lng);
                postLocation(lat, lng);
                if (ref) {
                    ref.invokeMethodAsync('OnUserLocation', lat, lng).catch(() => {});
                }
                // Kontinuierliches Tracking neu starten
                startWatchingUser(ref);
            },
            err => {
                console.warn('Geolocation permission request failed', err);
                if (ref) {
                    ref.invokeMethodAsync('OnUserLocationError', err.code).catch(() => {});
                }
            },
            { enableHighAccuracy: true, timeout: 15000 }
        );
    }

    function stopWatchingUser() {
        clearWatch();
        dotNetRef = null;
    }

    function getAreaCentroid() {
        if (!polygonLayer) return null;
        try {
            const c = polygonLayer.getBounds().getCenter();
            return { lat: c.lat, lng: c.lng };
        } catch (e) { return null; }
    }

    function postLocation(lat, lng) {
        fetch('/api/team-mobile/location', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ lat, lng })
        })
            .then(response => {
                if (!response.ok) {
                    console.warn('TeamMobile location upload failed', response.status);
                }
            })
            .catch(err => {
                console.warn('TeamMobile location upload error', err);
            });
    }

    return {
        init, renderSearchArea, setDogPosition, setTrack, appendTrackPoint,
        setUserPosition, appendUserTrackPoint, loadUserTrack, addCompletedTrack,
        reloadCurrentTracks,
        centerOnDog, destroy,
        startWatchingUser, stopWatchingUser, requestGeolocationPermission,
        getAreaCentroid, postLocation, setOptions
    };
})();
