window.einsatzMap = (function () {
    let map = null;
    let areasLayer = null;
    let elwMarker = null;
    let draftLayer = null;
    let draftPolyline = null;
    let draftPoints = [];
    let drawMode = false;
    let baseLayerControl = null;
    let currentBaseLayer = null;
    let baseLayers = {};

    function ensureMap(containerId, centerLat, centerLng, zoom) {
        if (map) {
            return;
        }

        map = L.map(containerId, {
            zoomControl: true,
            preferCanvas: true
        }).setView([centerLat, centerLng], zoom);

        const streetLayer = L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: "&copy; OpenStreetMap"
        });

        const satelliteLayer = L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}", {
            maxZoom: 19,
            attribution: "Tiles &copy; Esri"
        });

        const labelsLayer = L.tileLayer("https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}", {
            maxZoom: 19,
            attribution: "Labels &copy; Esri"
        });

        const hybridLayer = L.layerGroup([satelliteLayer, labelsLayer]);

        const topoLayer = L.tileLayer("https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png", {
            maxZoom: 17,
            attribution: 'Kartendaten: &copy; <a href="https://openstreetmap.org/copyright">OpenStreetMap</a> | Kartenstil: &copy; <a href="https://opentopomap.org">OpenTopoMap</a> (<a href="https://creativecommons.org/licenses/by-sa/3.0/">CC-BY-SA</a>)'
        });

        streetLayer.addTo(map);
        currentBaseLayer = streetLayer;
        baseLayers = {
            streets: streetLayer,
            satellite: satelliteLayer,
            hybrid: hybridLayer,
            topo: topoLayer
        };
        baseLayerControl = L.control.layers(
            {
                "Strassenkarte": streetLayer,
                "Satellit": satelliteLayer,
                "Hybrid": hybridLayer,
                "Topografisch": topoLayer
            },
            null,
            { collapsed: false }
        ).addTo(map);

        areasLayer = L.layerGroup().addTo(map);
        draftLayer = L.layerGroup().addTo(map);

        L.control.scale({ imperial: false, position: 'bottomleft' }).addTo(map);

        // Lade-Skelett + Kachel-Fehlerbanner (in den Leaflet-Container injiziert,
        // daher außerhalb des Blazor-DOM — Blazor fasst #einsatzMap-Inhalt nicht an)
        wireTileStatus(containerId, [streetLayer, satelliteLayer, labelsLayer, topoLayer]);

        map.on("click", function (event) {
            if (!drawMode) {
                return;
            }

            addDraftPoint(event.latlng.lat, event.latlng.lng);
        });
    }

    function wireTileStatus(containerId, layers) {
        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        const skeleton = document.createElement("div");
        skeleton.className = "map-loading-overlay";
        skeleton.innerHTML =
            '<div class="map-loading-card" role="status" aria-live="polite">' +
            '<i class="bi bi-map"></i><span>Karte wird geladen…</span></div>';
        container.appendChild(skeleton);

        const errBanner = document.createElement("div");
        errBanner.className = "map-tile-error";
        errBanner.setAttribute("role", "alert");
        errBanner.hidden = true;
        errBanner.innerHTML =
            '<i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>' +
            '<span>Kartenkacheln konnten nicht geladen werden — Netzwerk/VPN prüfen.</span>' +
            '<button type="button" class="map-tile-error-close" aria-label="Schließen">&times;</button>';
        container.appendChild(errBanner);
        errBanner.querySelector(".map-tile-error-close")
            .addEventListener("click", function () { errBanner.hidden = true; });

        let resolved = false;
        function hideSkeleton() {
            if (resolved) {
                return;
            }
            resolved = true;
            skeleton.remove();
        }

        // Aktive Basisschicht (streets) meldet 'load', wenn alle sichtbaren Kacheln da sind
        if (layers[0]) {
            layers[0].on("load", hideSkeleton);
        }
        // Sicherheitsnetz: Skelett nie länger als 8 s stehen lassen
        setTimeout(hideSkeleton, 8000);

        layers.forEach(function (layer) {
            if (layer && typeof layer.on === "function") {
                layer.on("tileerror", function () {
                    hideSkeleton();
                    errBanner.hidden = false;
                });
            }
        });
    }

    function init(containerId, centerLat, centerLng, zoom) {
        ensureMap(containerId, centerLat, centerLng, zoom);

        if (elwMarker) {
            elwMarker.remove();
        }

        elwMarker = L.marker([centerLat, centerLng]).addTo(map);
        elwMarker.bindPopup("ELW / Einsatzleitung");
    }

    function renderAreas(areas) {
        if (!map || !areasLayer) {
            return;
        }

        areasLayer.clearLayers();

        for (const area of areas || []) {
            const coordinates = (area.coordinates || [])
                .map(c => [c.latitude, c.longitude]);

            if (coordinates.length < 3) {
                continue;
            }

            const polygon = L.polygon(coordinates, {
                color: area.color || "#2196F3",
                fillOpacity: area.isCompleted ? 0.2 : 0.35,
                weight: 2
            }).addTo(areasLayer);

            const assigned = area.assignedTeamName
                ? `<br/>Team: ${area.assignedTeamName}`
                : "";

            polygon.bindPopup(`<strong>${area.name || "Suchgebiet"}</strong>${assigned}`);
        }
    }

    function renderDraft() {
        if (!map || !draftLayer) {
            return;
        }

        draftLayer.clearLayers();

        if (!draftPoints || draftPoints.length === 0) {
            return;
        }

        const polylineCoordinates = draftPoints.map(p => [p.latitude, p.longitude]);

        draftPolyline = L.polyline(polylineCoordinates, {
            color: "#ff9800",
            weight: 3,
            dashArray: "6 6"
        }).addTo(draftLayer);

        if (draftPoints.length >= 3) {
            L.polygon(polylineCoordinates, {
                color: "#ff9800",
                fillColor: "#ff9800",
                fillOpacity: 0.2,
                weight: 2
            }).addTo(draftLayer);
        }

        draftPoints.forEach((point, index) => {
            const marker = L.circleMarker([point.latitude, point.longitude], {
                radius: 6,
                color: "#ff9800",
                fillColor: "#fff3cd",
                fillOpacity: 1,
                weight: 2
            }).addTo(draftLayer);

            marker.bindTooltip(`P${index + 1}`, {
                direction: "top",
                offset: [0, -6]
            });

            marker.on("mousedown", function () {
                map.dragging.disable();
            });

            marker.on("mouseup", function () {
                map.dragging.enable();
            });

            marker.on("mousemove", function (event) {
                if (!drawMode || !event.originalEvent.buttons) {
                    return;
                }

                draftPoints[index] = {
                    latitude: event.latlng.lat,
                    longitude: event.latlng.lng
                };

                renderDraft();
            });
        });
    }

    function addDraftPoint(latitude, longitude) {
        draftPoints.push({ latitude, longitude });
        renderDraft();
    }

    function startDraft(coordinates) {
        drawMode = true;
        draftPoints = (coordinates || []).map(c => ({
            latitude: c.latitude,
            longitude: c.longitude
        }));
        renderDraft();
    }

    function clearDraft() {
        draftPoints = [];
        drawMode = false;

        if (draftLayer) {
            draftLayer.clearLayers();
        }
    }

    function undoDraftPoint() {
        if (!draftPoints || draftPoints.length === 0) {
            return;
        }

        draftPoints.pop();
        renderDraft();
    }

    function getDraftCoordinates() {
        return draftPoints.map(p => ({
            latitude: p.latitude,
            longitude: p.longitude
        }));
    }

    function setView(centerLat, centerLng, zoom) {
        if (!map) {
            return;
        }

        map.setView([centerLat, centerLng], zoom);
    }

    function setBaseLayer(layerType) {
        if (!map || !baseLayers || !baseLayers.streets) {
            return;
        }

        const requested = baseLayers[layerType] || baseLayers.streets;
        if (currentBaseLayer === requested) {
            return;
        }

        if (currentBaseLayer) {
            map.removeLayer(currentBaseLayer);
        }

        requested.addTo(map);
        currentBaseLayer = requested;
    }

    function recenter() {
        if (!map) return;
        let combined = null;
        try {
            if (areasLayer && areasLayer.getLayers().length > 0) {
                const b = areasLayer.getBounds();
                if (b && b.isValid()) combined = combined ? combined.extend(b) : b;
            }
            // Take all map layers that have getBounds (markers, polygons)
            map.eachLayer(function (layer) {
                if (typeof layer.getBounds === "function" && layer !== areasLayer && layer !== draftLayer) {
                    try {
                        const b = layer.getBounds();
                        if (b && b.isValid && b.isValid()) {
                            combined = combined ? combined.extend(b) : b;
                        }
                    } catch (e) { /* layer ohne valid bounds */ }
                }
                // Marker (kein getBounds aber LatLng)
                if (typeof layer.getLatLng === "function") {
                    try {
                        const ll = layer.getLatLng();
                        const b = L.latLngBounds([ll, ll]);
                        combined = combined ? combined.extend(b) : b;
                    } catch (e) { /* kein latlng */ }
                }
            });
        } catch (e) { /* swallow */ }

        if (combined && combined.isValid()) {
            map.fitBounds(combined, { padding: [40, 40], maxZoom: 16 });
        }
    }

    return {
        init,
        renderAreas,
        setView,
        setBaseLayer,
        recenter,
        startDraft,
        clearDraft,
        undoDraftPoint,
        getDraftCoordinates
    };
})();
