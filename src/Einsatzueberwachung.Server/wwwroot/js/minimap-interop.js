'use strict';

window.minimapInterop = (function () {
    const _instances = {};

    function init(containerId) {
        destroy(containerId);

        const el = document.getElementById(containerId);
        if (!el || typeof L === 'undefined') return;

        const map = L.map(containerId, {
            zoomControl: true,
            attributionControl: false,
            scrollWheelZoom: true,
            doubleClickZoom: true,
            dragging: true
        }).setView([51.5, 10.3], 7);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);

        _instances[containerId] = {
            map,
            elwMarker: null,
            areaLayers: [],
            initialized: true
        };
    }

    function updateElw(containerId, lat, lng) {
        const inst = _instances[containerId];
        if (!inst) return;

        const pos = [lat, lng];

        if (inst.elwMarker) {
            inst.elwMarker.setLatLng(pos);
        } else {
            const icon = L.divIcon({
                className: '',
                html: '<div style="background:#1e88e5;border:2px solid white;border-radius:50%;width:14px;height:14px;box-shadow:0 1px 4px rgba(0,0,0,.4)"></div>',
                iconSize: [14, 14],
                iconAnchor: [7, 7]
            });
            inst.elwMarker = L.marker(pos, { icon, title: 'ELW-Position', zIndexOffset: 1000 })
                .bindTooltip('ELW', { permanent: false })
                .addTo(inst.map);
        }

        if (inst.areaLayers.length === 0) {
            inst.map.setView(pos, 13);
        }
    }

    function updateSearchAreas(containerId, areas) {
        const inst = _instances[containerId];
        if (!inst) return;

        inst.areaLayers.forEach(l => inst.map.removeLayer(l));
        inst.areaLayers = [];

        const bounds = [];

        areas.forEach(function (area) {
            if (!area.coordinates || area.coordinates.length < 3) return;

            const coords = area.coordinates.map(function (c) { return [c[0], c[1]]; });
            const color = area.isCompleted ? '#6c757d' : (area.color || '#2196F3');

            const polygon = L.polygon(coords, {
                color: color,
                fillColor: color,
                fillOpacity: area.isCompleted ? 0.1 : 0.2,
                weight: 2,
                dashArray: area.isCompleted ? '6 4' : null
            }).bindTooltip(escapeHtml(area.name) + (area.teamName ? '<br>' + escapeHtml(area.teamName) : ''), {
                permanent: false,
                sticky: true
            }).addTo(inst.map);

            inst.areaLayers.push(polygon);
            coords.forEach(function (c) { bounds.push(c); });
        });

        if (bounds.length > 0) {
            try { inst.map.fitBounds(bounds, { padding: [20, 20] }); } catch (e) { }
        }
    }

    function invalidate(containerId) {
        const inst = _instances[containerId];
        if (inst) {
            setTimeout(function () { inst.map.invalidateSize(); }, 50);
        }
    }

    function destroy(containerId) {
        const inst = _instances[containerId];
        if (inst) {
            try { inst.map.remove(); } catch (e) { }
            delete _instances[containerId];
        }
    }

    return { init, updateElw, updateSearchAreas, invalidate, destroy };
})();
