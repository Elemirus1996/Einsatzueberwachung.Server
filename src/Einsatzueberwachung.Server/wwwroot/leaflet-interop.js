// Leaflet Map Interop fuer Einsatzueberwachung
// Ermoeglicht das Zeichnen und Verwalten von Suchgebieten auf einer interaktiven Karte
// Debug-Flag - setze auf false fuer Production
const DEBUG = false;
const log = DEBUG ? console.log.bind(console) : () => {};
const error = console.error.bind(console); // Errors immer loggen
window.LeafletMap = {
maps: {},

setSearchAreaMetadata: function(layer, areaId) {
    if (!layer || !areaId) {
        return;
    }

    layer._searchAreaId = areaId;

    if (layer.feature) {
        layer.feature.properties = layer.feature.properties || {};
        layer.feature.properties.searchAreaId = areaId;
    }

    if (typeof layer.eachLayer === 'function') {
        layer.eachLayer((childLayer) => {
            this.setSearchAreaMetadata(childLayer, areaId);
        });
    }
},

getSearchAreaId: function(layer) {
    if (!layer) {
        return '';
    }

    if (layer._searchAreaId) {
        return layer._searchAreaId;
    }

    if (layer.feature && layer.feature.properties && layer.feature.properties.searchAreaId) {
        return layer.feature.properties.searchAreaId;
    }

    return '';
},
    
// Initialisiert eine neue Karte
initialize: function(mapId, centerLat, centerLng, zoom, dotNetReference) {
    try {
        // Karte erstellen
        const map = L.map(mapId).setView([centerLat, centerLng], zoom);
            
        // Layer definieren
        const osmLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '(c) OpenStreetMap contributors',
            maxZoom: 19
        });
        
        // Esri Satellite Layer
        const satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
            attribution: 'Tiles (c) Esri',
            maxZoom: 18
        });
        
        // Google Satellite als Fallback (falls Esri nicht laedt)
        const googleSatellite = L.tileLayer('https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}', {
            attribution: 'Map data (c) Google',
            maxZoom: 20
        });
        
        // Hybrid: Satellit mit Strassennamen
        const hybridLayer = L.tileLayer('https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}', {
            attribution: 'Map data (c) Google',
            maxZoom: 20
        });
        
        log('Layer erstellt');
        
        // Standard-Layer hinzufuegen
        osmLayer.addTo(map);
        
        // Layer Control mit besserer Konfiguration
        const baseMaps = {
            "Strassenkarte": osmLayer,
            "Satellit (Esri)": satelliteLayer,
            "Satellit (Google)": googleSatellite,
            "Hybrid (Google)": hybridLayer
        };
        
        log('Layer Control wird hinzugefuegt');
        const layerControl = L.control.layers(baseMaps, null, {
            position: 'topright',
            collapsed: false  // Immer ausgeklappt fuer bessere Sichtbarkeit
        });
        layerControl.addTo(map);
        log('Layer Control hinzugeFuegt:', layerControl);
            
        // FeatureGroup fuer gezeichnete Items (NUR neue, ungespeicherte Zeichnungen!)
        const drawnItems = new L.FeatureGroup();
        map.addLayer(drawnItems);
        
        // NEUE FeatureGroup für gespeicherte Suchgebiete (SEPARATE Layer!)
        const savedAreas = new L.FeatureGroup();
        map.addLayer(savedAreas);
            
        // Draw Control erstellen
        const drawControl = new L.Control.Draw({
            edit: {
                featureGroup: drawnItems,
                edit: true,
                remove: true
            },
            draw: {
                polygon: {
                    allowIntersection: false,
                    showArea: true,
                    drawError: {
                        color: '#e74c3c',
                        message: '<strong>Fehler!</strong> Polygone duerfen sich nicht ueberschneiden.'
                    },
                    shapeOptions: {
                        color: '#3388ff',
                        weight: 3
                    }
                },
                polyline: false,
                rectangle: true,
                circle: false,
                circlemarker: false,
                marker: true
            }
        });
        // Draw Control zur Karte hinzufügen (nötig für interne Funktionalität)
        // Die Buttons werden via CSS versteckt und über externe Buttons gesteuert
        map.addControl(drawControl);
            
        // Event-Listener fuer gezeichnete Shapes
        map.on(L.Draw.Event.CREATED, function(e) {
            const layer = e.layer;
            drawnItems.addLayer(layer);
                
            // Callback an Blazor
            const geoJSON = layer.toGeoJSON();
            if (dotNetReference) {
                dotNetReference.invokeMethodAsync('OnShapeCreated', JSON.stringify(geoJSON));
            }
        });
            
        // Event-Listener fuer bearbeitete Shapes
        map.on(L.Draw.Event.EDITED, function(e) {
            const layers = e.layers;
            layers.eachLayer(function(layer) {
                const geoJSON = layer.toGeoJSON();
                const areaId = window.LeafletMap.getSearchAreaId(layer);
                if (dotNetReference) {
                    dotNetReference.invokeMethodAsync('OnShapeEdited', areaId, JSON.stringify(geoJSON));
                }
            });
        });
            
        // Event-Listener fuer geloeschte Shapes
        map.on(L.Draw.Event.DELETED, function(e) {
            const layers = e.layers;
            layers.eachLayer(function(layer) {
                const geoJSON = layer.toGeoJSON();
                const areaId = window.LeafletMap.getSearchAreaId(layer);
                if (dotNetReference) {
                    dotNetReference.invokeMethodAsync('OnShapeDeleted', areaId, JSON.stringify(geoJSON));
                }
            });
        });
            
        // Karte speichern
        this.maps[mapId] = {
            map: map,
            drawnItems: drawnItems,
            savedAreas: savedAreas,
            drawControl: drawControl,
            markers: {},
            dotNetReference: dotNetReference,
            layers: {
                streets: osmLayer,
                satellite: satelliteLayer,
                satelliteGoogle: googleSatellite,
                hybrid: hybridLayer
            },
            currentLayer: osmLayer
        };

        // ---- Live-Flächen-Anzeige als Leaflet-Tooltip (zentriert im Polygon) ----
        // Hilfsfunktion: flaches LatLng-Array aus Polygon-getLatLngs() (kann [[...]] sein)
        const getFlat = (latLngs) => {
            if (!latLngs || latLngs.length === 0) return [];
            return (latLngs[0] && latLngs[0].lat === undefined) ? latLngs[0] : latLngs;
        };
        // Hilfsfunktion: geometrischer Schwerpunkt (Mittelwert) der Koordinaten
        const calcCentroid = (latLngs) => {
            let lat = 0, lng = 0;
            const n = latLngs.length;
            for (let i = 0; i < n; i++) { lat += latLngs[i].lat; lng += latLngs[i].lng; }
            return L.latLng(lat / n, lng / n);
        };

        const liveAreaTooltip = L.tooltip({ permanent: true, direction: 'center', className: 'live-area-tooltip' });

        const updateLiveArea = (rawLatLngs) => {
            const flat = getFlat(rawLatLngs);
            if (!flat || flat.length < 3) return;
            const area = window.LeafletMap.calcGeodesicArea(flat);
            liveAreaTooltip.setLatLng(calcCentroid(flat))
                           .setContent('\u{1F4D0} ' + window.LeafletMap.formatArea(area));
            if (!map.hasLayer(liveAreaTooltip)) liveAreaTooltip.addTo(map);
        };
        const hideLiveArea = () => {
            if (map.hasLayer(liveAreaTooltip)) liveAreaTooltip.remove();
        };

        let drawModeActive = false;

        // Draw-Modus: Vertex gesetzt → Fläche der bestätigten Punkte anzeigen
        map.on('draw:drawvertex', function() {
            const polyMode = drawControl._toolbars.draw._modes.polygon;
            if (!polyMode || !polyMode.handler._poly) return;
            updateLiveArea(polyMode.handler._poly.getLatLngs());
        });

        // Draw-Modus: Mausbewegung → vorläufige Fläche mit Cursor-Position anzeigen
        map.on('mousemove', function(e) {
            if (!drawModeActive) return;
            const polyMode = drawControl._toolbars.draw._modes.polygon;
            if (!polyMode || !polyMode.handler._enabled || !polyMode.handler._poly) return;
            const existing = polyMode.handler._poly.getLatLngs();
            if (existing.length < 2) return; // mindestens 2 Punkte + Cursor = 3 Ecken nötig
            updateLiveArea(existing.concat([e.latlng]));
        });

        // Draw aktiviert / deaktiviert
        map.on('draw:drawstart', function() { drawModeActive = true; });
        map.on('draw:drawstop', function() {
            drawModeActive = false;
            hideLiveArea();
        });

        // Edit beendet (Speichern oder Abbrechen) → ausblenden
        map.on('draw:editstop', function() { hideLiveArea(); });
        // ---- Ende Live-Flächen-Anzeige ----

        // Live-Flächen-Hilfsfunktionen für Nutzung in startPolygonEdit speichern
        this.maps[mapId].updateLiveArea = updateLiveArea;
        this.maps[mapId].hideLiveArea = hideLiveArea;

        return true;
    } catch (error) {
        error('Fehler beim Initialisieren der Karte:', error);
        return false;
    }
},
    
    // Fuegt ein Suchgebiet (Polygon) zur Karte hinzu
    addSearchArea: function(mapId, areaId, geoJSON, color, name) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            const layer = L.geoJSON(JSON.parse(geoJSON), {
                style: {
                    color: color,
                    weight: 3,
                    opacity: 0.8,
                    fillOpacity: 0.3
                }
            });
            
            layer.bindPopup(`<strong>${name}</strong>`);
            // WICHTIG: Zu savedAreas hinzufügen (NICHT drawnItems!)
            // Das verhindert, dass gespeicherte Gebiete gelöscht werden
            layer.addTo(mapData.savedAreas);
            this.setSearchAreaMetadata(layer, areaId);
            
            // Layer-ID speichern
            mapData.markers[areaId] = layer;
            
            return true;
        } catch (error) {
            error('Fehler beim Hinzufuegen des Suchgebiets:', error);
            return false;
        }
    },
    
    // Entfernt ein Suchgebiet von der Karte
    removeSearchArea: function(mapId, areaId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData || !mapData.markers[areaId]) return false;
            
            const layer = mapData.markers[areaId];
            
            // Versuche aus beiden Gruppen zu entfernen (savedAreas oder drawnItems)
            if (mapData.savedAreas.hasLayer(layer)) {
                mapData.savedAreas.removeLayer(layer);
            } else if (mapData.drawnItems.hasLayer(layer)) {
                mapData.drawnItems.removeLayer(layer);
            }
            
            delete mapData.markers[areaId];
            
            return true;
        } catch (error) {
            error('Fehler beim Entfernen des Suchgebiets:', error);
            return false;
        }
    },
    
    // Setzt einen Marker auf der Karte
    setMarker: function(mapId, markerId, lat, lng, title, iconColor) {
        log('setMarker aufgerufen:', { mapId, markerId, lat, lng, title });
        
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }
            
            log('Karten-Daten gefunden:', mapData);
            
            // WICHTIG: Alten Marker IMMER entfernen (damit ELW-Position aktualisiert werden kann)
            if (mapData.markers[markerId]) {
                log('Entferne alten Marker:', markerId);
                mapData.map.removeLayer(mapData.markers[markerId]);
                delete mapData.markers[markerId];  // Aus der Liste entfernen
            }
            
            // Icon fuer ELW (spezielle rote Darstellung)
            let markerIcon = null;
            
            if (markerId === 'elw') {
                log('Erstelle ROTES ELW-Icon');
                // Erstelle einen roten Marker fuer ELW
                const svgIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="45" viewBox="0 0 32 45">
                    <path fill="#DC143C" stroke="#8B0000" stroke-width="2" d="M16 0 C7 0 0 7 0 16 C0 28 16 45 16 45 C16 45 32 28 32 16 C32 7 25 0 16 0 Z"/>
                    <circle cx="16" cy="16" r="8" fill="white"/>
                    <text x="16" y="22" font-size="14" font-weight="bold" text-anchor="middle" fill="#DC143C" font-family="Arial">E</text>
                </svg>`;
                
                markerIcon = L.divIcon({
                    html: svgIcon,
                    iconSize: [32, 45],
                    iconAnchor: [16, 45],
                    popupAnchor: [0, -45],
                    className: 'elw-marker-icon'
                });
            } else if (markerId === 'einsatzort') {
                log('Erstelle Standard Einsatzort-Icon');
                markerIcon = L.icon({
                    iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
                    shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
                    iconSize: [25, 41],
                    iconAnchor: [12, 41],
                    popupAnchor: [1, -34],
                    shadowSize: [41, 41]
                });
            } else {
                log('Erstelle Standard-Icon');
                markerIcon = L.icon({
                    iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
                    shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
                    iconSize: [25, 41],
                    iconAnchor: [12, 41],
                    popupAnchor: [1, -34],
                    shadowSize: [41, 41]
                });
            }
            
            log('Erstelle Marker an Position:', lat, lng);
            
            // Neuen Marker erstellen
            const marker = L.marker([lat, lng], {
                icon: markerIcon,
                title: title,
                draggable: markerId === 'elw'  // ELW-Marker kann verschoben werden
            }).addTo(mapData.map);
            
            // Popup
            marker.bindPopup(`<strong>${title}</strong><br><small>Lat: ${lat.toFixed(5)}, Lng: ${lng.toFixed(5)}</small>`);
            
            // Bei Verschiebung von ELW: Update Position
            if (markerId === 'elw') {
                marker.on('dragend', function(e) {
                    const newPos = e.target.getLatLng();
                    log('ELW verschoben zu:', newPos);
                    marker.setPopupContent(`<strong>${title}</strong><br><small>Lat: ${newPos.lat.toFixed(5)}, Lng: ${newPos.lng.toFixed(5)}</small>`);
                    
                    // Informiere Blazor ueber neue Position
                    if (mapData.dotNetReference) {
                        mapData.dotNetReference.invokeMethodAsync('OnElwMarkerMoved', newPos.lat, newPos.lng);
                    }
                });
            }
            
            // Marker speichern
            mapData.markers[markerId] = marker;
            
            log('Marker erfolgreich erstellt:', markerId);
            
            return true;
        } catch (error) {
            error('Fehler beim Setzen des Markers:', error);
            return false;
        }
    },
    
    // Entfernt einen Marker
    removeMarker: function(mapId, markerId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData || !mapData.markers[markerId]) return false;
            
            mapData.map.removeLayer(mapData.markers[markerId]);
            delete mapData.markers[markerId];
            
            return true;
        } catch (error) {
            error('Fehler beim Entfernen des Markers:', error);
            return false;
        }
    },
    
    // Zentriert die Karte auf eine Position
    centerMap: function(mapId, lat, lng, zoom) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            mapData.map.setView([lat, lng], zoom || mapData.map.getZoom());
            return true;
        } catch (error) {
            error('Fehler beim Zentrieren der Karte:', error);
            return false;
        }
    },
    
    // Gibt die aktuelle Kartenmitte zurueck
    getMapCenter: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return { lat: 0, lng: 0 };
            
            const center = mapData.map.getCenter();
            return {
                lat: center.lat,
                lng: center.lng
            };
        } catch (error) {
            error('Fehler beim Abrufen der Kartenmitte:', error);
            return { lat: 0, lng: 0 };
        }
    },
    
    // Wechselt die Basis-Layer-Ansicht
    changeBaseLayer: function(mapId, layerType) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }
            
            // Entferne aktuellen Layer
            if (mapData.currentLayer) {
                mapData.map.removeLayer(mapData.currentLayer);
            }
            
            // Fuege neuen Layer hinzu
            let newLayer = null;
            switch(layerType) {
                case 'streets':
                    newLayer = mapData.layers.streets;
                    break;
                case 'satellite':
                    newLayer = mapData.layers.satellite;
                    break;
                case 'hybrid':
                    newLayer = mapData.layers.hybrid;
                    break;
                default:
                    newLayer = mapData.layers.streets;
            }
            
            if (newLayer) {
                newLayer.addTo(mapData.map);
                mapData.currentLayer = newLayer;
                log('Layer gewechselt zu:', layerType);
            }
            
            return true;
        } catch (error) {
            error('Fehler beim Wechseln des Layers:', error);
            return false;
        }
    },
    
    // Passt den Kartenausschnitt an alle Marker an
    fitBounds: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            const group = new L.featureGroup(Object.values(mapData.markers));
            mapData.map.fitBounds(group.getBounds().pad(0.1));
            
            return true;
        } catch (error) {
            error('Fehler beim Anpassen der Bounds:', error);
            return false;
        }
    },
    
    // Passt Kartenausschnitt an ALLE Elemente an (Marker + Suchgebiete)
    fitAllElements: function(mapId, padding) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            const allLayers = [];
            const pad = padding || 0.15; // Standard 15% Padding
            
            // Alle Marker sammeln
            if (mapData.markers) {
                Object.values(mapData.markers).forEach(marker => {
                    if (marker) allLayers.push(marker);
                });
            }
            
            // Alle gespeicherten Suchgebiete aus savedAreas
            if (mapData.savedAreas) {
                mapData.savedAreas.eachLayer(layer => {
                    // Nur hinzufügen wenn nicht schon in allLayers
                    if (!allLayers.includes(layer)) {
                        allLayers.push(layer);
                    }
                });
            }
            
            // Alle neuen, ungespeicherten Zeichnungen aus drawnItems
            if (mapData.drawnItems) {
                mapData.drawnItems.eachLayer(layer => {
                    // Nur hinzufügen wenn nicht schon in allLayers
                    if (!allLayers.includes(layer)) {
                        allLayers.push(layer);
                    }
                });
            }
            
            console.log('fitAllElements: Gefundene Elemente:', allLayers.length);
            
            if (allLayers.length === 0) {
                console.log('Keine Elemente zum Zentrieren gefunden');
                return false;
            }
            
            const group = new L.featureGroup(allLayers);
            const bounds = group.getBounds();
            
            if (bounds.isValid()) {
                // fitBounds mit maxZoom begrenzen damit nicht zu nah rangezoomt wird
                mapData.map.fitBounds(bounds.pad(pad), {
                    maxZoom: 16,
                    animate: false
                });
                console.log('Karte auf', allLayers.length, 'Elemente zentriert, Bounds:', bounds.toBBoxString());
                return true;
            }
            
            return false;
        } catch (err) {
            console.error('Fehler beim Anpassen aller Bounds:', err);
            return false;
        }
    },
    
    // Geocoding: Adresse zu Koordinaten
    geocodeAddress: async function(address) {
        try {
            const response = await fetch(`https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(address)}&limit=1`);
            const data = await response.json();
            
            if (data && data.length > 0) {
                return {
                    success: true,
                    lat: parseFloat(data[0].lat),
                    lng: parseFloat(data[0].lon),
                    displayName: data[0].display_name
                };
            }
            
            return { success: false, message: 'Adresse nicht gefunden' };
        } catch (error) {
            error('Fehler beim Geocoding:', error);
            return { success: false, message: error.message };
        }
    },
    
    // Druckt die Karte
    printMap: function(mapId) {
        log('printMap aufgerufen für:', mapId);
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }

            const map = mapData.map;
            const removedLayers = [];
            const hadTrackingLayerVisible = !!(mapData.trackingLayer && map.hasLayer(mapData.trackingLayer));
            const hadDrawnItemsVisible = !!(mapData.drawnItems && map.hasLayer(mapData.drawnItems));
            const root = document.documentElement;
            const body = document.body;
            const printListTemplate = document.querySelector('.print-list-template');
            let printListHost = null;

            const currentCenter = map.getCenter();
            const currentZoom = map.getZoom();

            Object.entries(mapData.markers || {}).forEach(([markerId, layer]) => {
                const isSearchAreaLayer = mapData.savedAreas && mapData.savedAreas.hasLayer(layer);
                const shouldRemainVisible = markerId === 'elw' || isSearchAreaLayer;
                if (!shouldRemainVisible && map.hasLayer(layer)) {
                    map.removeLayer(layer);
                    removedLayers.push(layer);
                }
            });

            if (hadTrackingLayerVisible) {
                map.removeLayer(mapData.trackingLayer);
            }

            if (hadDrawnItemsVisible) {
                map.removeLayer(mapData.drawnItems);
            }

            const extendBoundsWithLatLngs = (bounds, latLngs) => {
                if (!latLngs) {
                    return;
                }

                latLngs.forEach((entry) => {
                    if (Array.isArray(entry)) {
                        extendBoundsWithLatLngs(bounds, entry);
                        return;
                    }

                    if (entry && typeof entry.lat === 'number' && typeof entry.lng === 'number') {
                        bounds.extend(entry);
                    }
                });
            };

            const collectPrintBounds = () => {
                const bounds = L.latLngBounds([]);

                if (mapData.savedAreas) {
                    mapData.savedAreas.eachLayer((layer) => {
                        if (!layer) {
                            return;
                        }

                        if (typeof layer.eachLayer === 'function' && !layer.getLatLngs && !layer.getLatLng) {
                            layer.eachLayer((childLayer) => {
                                if (childLayer && typeof childLayer.getLatLngs === 'function') {
                                    extendBoundsWithLatLngs(bounds, childLayer.getLatLngs());
                                } else if (childLayer && typeof childLayer.getLatLng === 'function') {
                                    bounds.extend(childLayer.getLatLng());
                                } else if (childLayer && typeof childLayer.getBounds === 'function') {
                                    bounds.extend(childLayer.getBounds());
                                }
                            });
                            return;
                        }

                        if (typeof layer.getLatLngs === 'function') {
                            extendBoundsWithLatLngs(bounds, layer.getLatLngs());
                            return;
                        }

                        if (typeof layer.getLatLng === 'function') {
                            bounds.extend(layer.getLatLng());
                            return;
                        }

                        if (typeof layer.getBounds === 'function') {
                            bounds.extend(layer.getBounds());
                        }
                    });
                }

                if (mapData.markers && mapData.markers.elw && typeof mapData.markers.elw.getLatLng === 'function') {
                    bounds.extend(mapData.markers.elw.getLatLng());
                }

                return bounds;
            };

            const bounds = collectPrintBounds();

            const applyPrintView = () => {
                map.invalidateSize();
                if (bounds.isValid()) {
                    map.fitBounds(bounds.pad(0.18), {
                        maxZoom: 16,
                        animate: false,
                        paddingTopLeft: [40, 40],
                        paddingBottomRight: [40, 40]
                    });

                    // Verschiebt den sichtbaren Druckbereich gezielt nach unten rechts.
                    map.panBy([350, 250], { animate: false });
                    return;
                }

                map.setView(currentCenter, currentZoom, { animate: false });
            };

            const enablePrintLayout = () => {
                root.classList.add('map-print-mode');
                body.classList.add('map-print-mode');

                if (!printListHost && printListTemplate) {
                    printListHost = printListTemplate.cloneNode(true);
                    printListHost.classList.remove('print-list-template');
                    printListHost.classList.add('print-list-page-host');
                    printListHost.removeAttribute('aria-hidden');
                    body.appendChild(printListHost);
                }
            };

            const disablePrintLayout = () => {
                root.classList.remove('map-print-mode');
                body.classList.remove('map-print-mode');

                if (printListHost && printListHost.parentNode) {
                    printListHost.parentNode.removeChild(printListHost);
                    printListHost = null;
                }
            };

            const beforePrintHandler = () => {
                enablePrintLayout();
                requestAnimationFrame(() => {
                    applyPrintView();

                    requestAnimationFrame(() => {
                        applyPrintView();
                    });
                });
            };

            const afterPrintHandler = () => {
                window.removeEventListener('beforeprint', beforePrintHandler);
                window.removeEventListener('afterprint', afterPrintHandler);

                removedLayers.forEach(layer => {
                    if (!map.hasLayer(layer)) {
                        map.addLayer(layer);
                    }
                });

                if (hadTrackingLayerVisible && mapData.trackingLayer && !map.hasLayer(mapData.trackingLayer)) {
                    map.addLayer(mapData.trackingLayer);
                }

                if (hadDrawnItemsVisible && mapData.drawnItems && !map.hasLayer(mapData.drawnItems)) {
                    map.addLayer(mapData.drawnItems);
                }

                disablePrintLayout();

                setTimeout(() => {
                    map.invalidateSize();
                    map.setView(currentCenter, currentZoom, { animate: false });
                }, 200);
            };

            window.addEventListener('beforeprint', beforePrintHandler);
            window.addEventListener('afterprint', afterPrintHandler);

            enablePrintLayout();

            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    applyPrintView();

                    setTimeout(() => {
                        map.invalidateSize();
                        applyPrintView();

                        setTimeout(() => {
                            log('Starte Druck-Dialog');
                            window.print();
                        }, 250);
                    }, 150);
                });
            });

            return true;
        } catch (err) {
            console.error('Fehler beim Drucken der Karte:', err);
            return false;
        }
    },
    
    // Exportiert die Karte als Bild (PNG)
    exportMapImage: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            // Hinweis fuer Benutzer
            alert('Tipp: Nutzen Sie "Karte drucken" und speichern Sie als PDF, oder machen Sie einen Screenshot (Windows: Win+Shift+S, Mac: Cmd+Shift+4)');
            return true;
        } catch (error) {
            error('Fehler beim Exportieren:', error);
            return false;
        }
    },
    
    // Kartengröße nach Container-Änderung aktualisieren
    invalidateSize: function(mapId) {
        const mapData = this.maps[mapId];
        if (mapData && mapData.map) {
            mapData.map.invalidateSize();
        }
    },

    // Karte aufraeumen
    dispose: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (mapData && mapData.map) {
                mapData.map.remove();
                delete this.maps[mapId];
            }
            return true;
        } catch (error) {
            error('Fehler beim Dispose der Karte:', error);
            return false;
        }
    },
    
    // Aktiviert einen spezifischen Draw-Modus (polygon, rectangle, marker)
    activateDrawMode: function(mapId, drawType) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                console.error('Karte nicht gefunden:', mapId);
                return false;
            }
            
            const drawControl = mapData.drawControl;
            if (!drawControl) {
                console.error('Draw Control nicht gefunden für Karte:', mapId);
                return false;
            }
            
            const drawMode = drawControl._toolbars && drawControl._toolbars.draw;
            if (!drawMode) {
                console.error('Draw Toolbar nicht verfügbar');
                return false;
            }
            
            // Aktiviere den spezifischen Draw-Modus gegenüber Leaflet Draw API
            switch(drawType.toLowerCase()) {
                case 'polygon':
                    if (drawMode._modes.polygon) {
                        drawMode._modes.polygon.handler.enable();
                        console.log('✓ Polygon-Modus aktiviert');
                    }
                    break;
                case 'rectangle':
                    if (drawMode._modes.rectangle) {
                        drawMode._modes.rectangle.handler.enable();
                        console.log('✓ Rechteck-Modus aktiviert');
                    }
                    break;
                case 'marker':
                    if (drawMode._modes.marker) {
                        drawMode._modes.marker.handler.enable();
                        console.log('✓ Marker-Modus aktiviert');
                    }
                    break;
                default:
                    console.error('Unbekannter Draw-Typ:', drawType);
                    return false;
            }
            
            return true;
        } catch (err) {
            console.error('Fehler beim Aktivieren des Draw-Modus:', err);
            console.error('Error stack:', err.stack);
            return false;
        }
    },
    
    // Aktiviert den Bearbeitungsmodus für ein bestehendes Suchgebiet
    startPolygonEdit: function(mapId, areaId) {
        const mapData = this.maps[mapId];
        if (!mapData) {
            console.error('Karte nicht gefunden:', mapId);
            return false;
        }

        try {
            const outerLayer = mapData.markers[areaId];
            if (!outerLayer) {
                console.error('Layer nicht gefunden für Area:', areaId);
                return false;
            }

            // Äußere GeoJSON-Gruppe aus savedAreas entfernen
            if (mapData.savedAreas.hasLayer(outerLayer)) {
                mapData.savedAreas.removeLayer(outerLayer);
            }

            // Inneren Polygon-Layer extrahieren
            let innerLayer = null;
            let innerLayerCount = 0;
            outerLayer.eachLayer(function(l) {
                innerLayerCount++;
                if (!innerLayer) innerLayer = l;
            });

            if (!innerLayer) {
                console.error('Kein innerer Layer gefunden für Area:', areaId);
                mapData.savedAreas.addLayer(outerLayer);
                return false;
            }

            if (innerLayerCount > 1) {
                console.warn('Mehrere innere Layer gefunden für Area:', areaId, '- nur der erste wird bearbeitet');
            }

            // Metadaten auf innerem Layer sicherstellen
            window.LeafletMap.setSearchAreaMetadata(innerLayer, areaId);

            // In drawnItems verschieben (wird von Leaflet.draw bearbeitet)
            mapData.drawnItems.addLayer(innerLayer);

            // Bearbeitungszustand speichern
            mapData.editingAreaId = areaId;
            mapData.editingOuterLayer = outerLayer;
            mapData.editingInnerLayer = innerLayer;

            // Live-Flächen-Anzeige: editdrag-Listener für Echtzeit-Updates beim Vertex-Ziehen
            if (mapData.updateLiveArea) {
                const onEditDrag = function() {
                    mapData.updateLiveArea(innerLayer.getLatLngs());
                };
                innerLayer.on('editdrag', onEditDrag);
                mapData._editDragHandler = { layer: innerLayer, fn: onEditDrag };
                // Fläche sofort beim Start der Bearbeitung anzeigen
                mapData.updateLiveArea(innerLayer.getLatLngs());
            }

            // Leaflet.draw Bearbeitungsmodus aktivieren
            const editHandler = mapData.drawControl._toolbars.edit._modes.edit.handler;
            editHandler.enable();

            console.log('Polygon-Bearbeitung gestartet für Area:', areaId);
            return true;
        } catch (e) {
            console.error('Fehler beim Starten des Polygon-Edits:', e);
            return false;
        }
    },

    // Speichert die Änderungen der Polygon-Bearbeitung
    savePolygonEdit: function(mapId) {
        const mapData = this.maps[mapId];
        if (!mapData) {
            console.error('Karte nicht gefunden:', mapId);
            return false;
        }

        try {
            const editHandler = mapData.drawControl._toolbars.edit._modes.edit.handler;
            // save() feuert L.Draw.Event.EDITED
            editHandler.save();

            // Inneren Layer aus drawnItems entfernen (OnShapeEdited fügt ihn über addSearchArea neu hinzu)
            if (mapData.editingInnerLayer && mapData.drawnItems.hasLayer(mapData.editingInnerLayer)) {
                mapData.drawnItems.removeLayer(mapData.editingInnerLayer);
            }

            // Bearbeitungszustand zurücksetzen
            mapData.editingAreaId = null;
            mapData.editingOuterLayer = null;
            mapData.editingInnerLayer = null;

            // Editdrag-Listener entfernen
            if (mapData._editDragHandler) {
                mapData._editDragHandler.layer.off('editdrag', mapData._editDragHandler.fn);
                mapData._editDragHandler = null;
            }

            // Edit-Modus beenden (feuert draw:editstop → blendet Flächen-Anzeige aus)
            editHandler.disable();

            return true;
        } catch (e) {
            console.error('Fehler beim Speichern des Polygon-Edits:', e);
            return false;
        }
    },

    // Bricht die Polygon-Bearbeitung ab und stellt den Originalzustand wieder her
    cancelPolygonEdit: function(mapId) {
        const mapData = this.maps[mapId];
        if (!mapData) {
            console.error('Karte nicht gefunden:', mapId);
            return false;
        }

        try {
            // Editdrag-Listener entfernen (vor revertLayers/disable)
            if (mapData._editDragHandler) {
                mapData._editDragHandler.layer.off('editdrag', mapData._editDragHandler.fn);
                mapData._editDragHandler = null;
            }

            const editHandler = mapData.drawControl._toolbars.edit._modes.edit.handler;
            editHandler.revertLayers();
            editHandler.disable();

            // Inneren Layer aus drawnItems entfernen und äußeren Layer wiederherstellen
            if (mapData.editingInnerLayer && mapData.drawnItems.hasLayer(mapData.editingInnerLayer)) {
                mapData.drawnItems.removeLayer(mapData.editingInnerLayer);
            }

            if (mapData.editingOuterLayer) {
                mapData.savedAreas.addLayer(mapData.editingOuterLayer);
            }

            // Bearbeitungszustand zurücksetzen
            mapData.editingAreaId = null;
            mapData.editingOuterLayer = null;
            mapData.editingInnerLayer = null;

            return true;
        } catch (e) {
            console.error('Fehler beim Abbrechen des Polygon-Edits:', e);
            return false;
        }
    },

    // Berechnet geodätische Fläche (m²) aus einem flachen Array von {lat, lng} Objekten.
    // Verwendet dieselbe sphärische Formel wie die C#-Klasse SearchArea.CalculatePolygonArea.
    calcGeodesicArea: function(latLngs) {
        if (!latLngs || latLngs.length < 3) return 0;
        const R = 6371000.0;
        let area = 0;
        const n = latLngs.length;
        for (let i = 0; i < n; i++) {
            const p1 = latLngs[i];
            const p2 = latLngs[(i + 1) % n];
            const lat1 = p1.lat * Math.PI / 180;
            const lat2 = p2.lat * Math.PI / 180;
            const lon1 = p1.lng * Math.PI / 180;
            const lon2 = p2.lng * Math.PI / 180;
            area += (lon2 - lon1) * (2 + Math.sin(lat1) + Math.sin(lat2));
        }
        return Math.abs(area * R * R / 2.0);
    },

    // Formatiert Fläche in m²/ha/km² (gleiche Schwellwerte wie C# FormattedArea).
    formatArea: function(sqm) {
        if (sqm < 1) return '< 1 m²';
        if (sqm < 50000) return Math.round(sqm).toLocaleString('de-DE') + ' m²';
        if (sqm < 1000000) return (sqm / 10000).toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ha';
        return (sqm / 1000000).toLocaleString('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' km²';
    },

    // Löscht alle Zeichnungen von der Karte
    clearAllDrawings: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData || !mapData.drawnItems) {
                error('Karte oder drawnItems nicht gefunden:', mapId);
                return false;
            }
            
            // Alle Layer aus drawnItems entfernen
            const layersToRemove = [];
            mapData.drawnItems.eachLayer(layer => {
                layersToRemove.push(layer);
            });
            
            layersToRemove.forEach(layer => {
                mapData.drawnItems.removeLayer(layer);
            });
            
            log('Alle Zeichnungen gelöscht, Anzahl:', layersToRemove.length);
            return true;
        } catch (err) {
            error('Fehler beim Löschen der Zeichnungen:', err);
            return false;
        }
    },

    // ========================================
    // Koordinaten-Marker Funktionen
    // ========================================

    // Setzt einen Koordinaten-Marker auf der Karte (Punkt mit Label)
    setCoordinateMarker: function(mapId, markerId, lat, lng, label, description, color) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }

            // Alten Marker entfernen falls vorhanden
            const coordMarkerId = 'coord_' + markerId;
            if (mapData.markers[coordMarkerId]) {
                mapData.map.removeLayer(mapData.markers[coordMarkerId]);
                delete mapData.markers[coordMarkerId];
            }

            const markerColor = color || '#2196F3';

            // SVG Pin-Icon mit Label-Nummer/Buchstabe
            const shortLabel = ((label && label.trim()) || '?').substring(0, 2);
            const svgIcon = `<svg xmlns="http://www.w3.org/2000/svg" width="30" height="42" viewBox="0 0 30 42">
                <path fill="${markerColor}" stroke="#333" stroke-width="1.5" d="M15 0 C7 0 0 6.5 0 15 C0 26 15 42 15 42 C15 42 30 26 30 15 C30 6.5 23 0 15 0 Z"/>
                <circle cx="15" cy="15" r="9" fill="white"/>
                <text x="15" y="20" font-size="12" font-weight="bold" text-anchor="middle" fill="${markerColor}" font-family="Arial">${shortLabel}</text>
            </svg>`;

            const icon = L.divIcon({
                html: svgIcon,
                iconSize: [30, 42],
                iconAnchor: [15, 42],
                popupAnchor: [0, -42],
                className: 'coordinate-marker-icon'
            });

            const marker = L.marker([lat, lng], {
                icon: icon,
                title: label || 'Koordinaten-Marker',
                draggable: false
            }).addTo(mapData.map);

            // Popup mit Koordinaten-Info
            const utmInfo = this._latLngToUtmString(lat, lng);
            const popupHtml = `<div class="coord-marker-popup">
                <strong>${label || 'Punkt'}</strong>
                ${description ? '<br><small>' + description + '</small>' : ''}
                <hr style="margin: 4px 0;">
                <small><strong>Lat/Long:</strong> ${lat.toFixed(6)}° / ${lng.toFixed(6)}°</small><br>
                <small><strong>UTM:</strong> ${utmInfo}</small>
            </div>`;
            marker.bindPopup(popupHtml);

            mapData.markers[coordMarkerId] = marker;
            return true;
        } catch (err) {
            error('Fehler beim Setzen des Koordinaten-Markers:', err);
            return false;
        }
    },

    // Entfernt einen Koordinaten-Marker
    removeCoordinateMarker: function(mapId, markerId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            const coordMarkerId = 'coord_' + markerId;
            if (mapData.markers[coordMarkerId]) {
                mapData.map.removeLayer(mapData.markers[coordMarkerId]);
                delete mapData.markers[coordMarkerId];
                return true;
            }
            return false;
        } catch (err) {
            error('Fehler beim Entfernen des Koordinaten-Markers:', err);
            return false;
        }
    },

    // Aktiviert den Klick-Modus zum Setzen eines Koordinaten-Markers
    enableCoordinateClickMode: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            // Cursor ändern
            mapData.map.getContainer().style.cursor = 'crosshair';

            // Suchgebiete temporär nicht-interaktiv machen, damit der Klick durchkommt
            if (mapData.savedAreas) {
                mapData.savedAreas.eachLayer(function(layer) {
                    if (layer.eachLayer) {
                        layer.eachLayer(function(subLayer) {
                            if (subLayer.getElement) {
                                const el = subLayer.getElement();
                                if (el) el.style.pointerEvents = 'none';
                            }
                        });
                    }
                    if (layer.getElement) {
                        const el = layer.getElement();
                        if (el) el.style.pointerEvents = 'none';
                    }
                });
            }

            // Einmaliger Klick-Handler
            const clickHandler = (e) => {
                const lat = e.latlng.lat;
                const lng = e.latlng.lng;

                // Cursor zurücksetzen
                mapData.map.getContainer().style.cursor = '';

                // Suchgebiete wieder interaktiv machen
                if (mapData.savedAreas) {
                    mapData.savedAreas.eachLayer(function(layer) {
                        if (layer.eachLayer) {
                            layer.eachLayer(function(subLayer) {
                                if (subLayer.getElement) {
                                    const el = subLayer.getElement();
                                    if (el) el.style.pointerEvents = '';
                                }
                            });
                        }
                        if (layer.getElement) {
                            const el = layer.getElement();
                            if (el) el.style.pointerEvents = '';
                        }
                    });
                }

                // Callback an Blazor
                if (mapData.dotNetReference) {
                    mapData.dotNetReference.invokeMethodAsync('OnCoordinateMarkerClicked', lat, lng)
                        .catch(err => error('Fehler beim Callback OnCoordinateMarkerClicked:', err));
                }
            };

            // Vorherigen Handler entfernen falls vorhanden
            if (mapData._coordClickHandler) {
                mapData.map.off('click', mapData._coordClickHandler);
            }
            mapData._coordClickHandler = clickHandler;
            mapData.map.once('click', clickHandler);

            return true;
        } catch (err) {
            error('Fehler beim Aktivieren des Klick-Modus:', err);
            return false;
        }
    },

    // Deaktiviert den Klick-Modus
    disableCoordinateClickMode: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            mapData.map.getContainer().style.cursor = '';
            if (mapData._coordClickHandler) {
                mapData.map.off('click', mapData._coordClickHandler);
                mapData._coordClickHandler = null;
            }

            // Suchgebiete wieder interaktiv machen
            if (mapData.savedAreas) {
                mapData.savedAreas.eachLayer(function(layer) {
                    if (layer.eachLayer) {
                        layer.eachLayer(function(subLayer) {
                            if (subLayer.getElement) {
                                const el = subLayer.getElement();
                                if (el) el.style.pointerEvents = '';
                            }
                        });
                    }
                    if (layer.getElement) {
                        const el = layer.getElement();
                        if (el) el.style.pointerEvents = '';
                    }
                });
            }

            return true;
        } catch (err) {
            error('Fehler beim Deaktivieren des Klick-Modus:', err);
            return false;
        }
    },

    // Aktiviert einmaligen Drag-Modus für einen Koordinaten-Marker.
    // Nach dem Drag wird draggable wieder deaktiviert und der Blazor-Callback aufgerufen.
    enableCoordinateMarkerDrag: function(mapId, markerId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            const coordMarkerId = 'coord_' + markerId;
            const marker = mapData.markers[coordMarkerId];
            if (!marker) return false;

            // Draggable aktivieren
            marker.dragging.enable();
            marker.getElement().style.cursor = 'grab';

            // Einmaliger Drag-Handler
            marker.once('dragend', (e) => {
                const newPos = e.target.getLatLng();

                // Draggable sofort wieder deaktivieren
                marker.dragging.disable();
                marker.getElement().style.cursor = '';

                // Popup aktualisieren
                const newUtmInfo = window.LeafletMap._latLngToUtmString(newPos.lat, newPos.lng);
                const title = marker.options.title || 'Punkt';
                const updatedPopup = `<div class="coord-marker-popup">
                    <strong>${title}</strong>
                    <hr style="margin: 4px 0;">
                    <small><strong>Lat/Long:</strong> ${newPos.lat.toFixed(6)}° / ${newPos.lng.toFixed(6)}°</small><br>
                    <small><strong>UTM:</strong> ${newUtmInfo}</small>
                </div>`;
                marker.setPopupContent(updatedPopup);

                // Callback an Blazor (Koordinaten nur als Vorschlag, nicht gespeichert)
                if (mapData.dotNetReference) {
                    mapData.dotNetReference.invokeMethodAsync('OnCoordinateMarkerDragCompleted', markerId, newPos.lat, newPos.lng)
                        .catch(err => error('Fehler beim Callback OnCoordinateMarkerDragCompleted:', err));
                }
            });

            return true;
        } catch (err) {
            error('Fehler beim Aktivieren des Marker-Drag-Modus:', err);
            return false;
        }
    },

    // Deaktiviert den Drag-Modus für einen Koordinaten-Marker (falls noch aktiv)
    disableCoordinateMarkerDrag: function(mapId, markerId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            const coordMarkerId = 'coord_' + markerId;
            const marker = mapData.markers[coordMarkerId];
            if (!marker) return false;

            marker.dragging.disable();
            if (marker.getElement()) {
                marker.getElement().style.cursor = '';
            }

            return true;
        } catch (err) {
            error('Fehler beim Deaktivieren des Marker-Drag-Modus:', err);
            return false;
        }
    },

    // Zentriert die Karte auf einen Koordinaten-Marker
    zoomToCoordinateMarker: function(mapId, markerId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            const coordMarkerId = 'coord_' + markerId;
            const marker = mapData.markers[coordMarkerId];
            if (marker) {
                const pos = marker.getLatLng();
                mapData.map.setView([pos.lat, pos.lng], 16);
                marker.openPopup();
                return true;
            }
            return false;
        } catch (err) {
            error('Fehler beim Zoomen zum Marker:', err);
            return false;
        }
    },

    // Hilfs-Funktion: Lat/Long zu UTM-String (vereinfachte JS-Implementierung)
    _latLngToUtmString: function(lat, lng) {
        try {
            const zone = Math.floor((lng + 180) / 6) + 1;
            const bands = 'CDEFGHJKLMNPQRSTUVWX';
            let bandIndex = Math.floor((lat + 80) / 8);
            if (bandIndex < 0) bandIndex = 0;
            if (bandIndex >= bands.length) bandIndex = bands.length - 1;
            const band = bands[bandIndex];

            // Vereinfachte UTM-Berechnung
            const a = 6378137.0;
            const e2 = 0.00669437999014;
            const k0 = 0.9996;
            const lonOrigin = (zone - 1) * 6 - 180 + 3;

            const latRad = lat * Math.PI / 180;
            const lonRad = lng * Math.PI / 180;
            const lonOriginRad = lonOrigin * Math.PI / 180;

            const ePrime2 = e2 / (1 - e2);
            const n = a / Math.sqrt(1 - e2 * Math.sin(latRad) * Math.sin(latRad));
            const t = Math.tan(latRad) * Math.tan(latRad);
            const c = ePrime2 * Math.cos(latRad) * Math.cos(latRad);
            const aa = Math.cos(latRad) * (lonRad - lonOriginRad);

            const m = a * (
                (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * latRad
                - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.sin(2 * latRad)
                + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.sin(4 * latRad)
                - (35 * e2 * e2 * e2 / 3072) * Math.sin(6 * latRad)
            );

            let easting = k0 * n * (
                aa + (1 - t + c) * aa * aa * aa / 6
                + (5 - 18 * t + t * t + 72 * c - 58 * ePrime2) * aa * aa * aa * aa * aa / 120
            ) + 500000;

            let northing = k0 * (
                m + n * Math.tan(latRad) * (
                    aa * aa / 2
                    + (5 - t + 9 * c + 4 * c * c) * aa * aa * aa * aa / 24
                    + (61 - 58 * t + t * t + 600 * c - 330 * ePrime2) * aa * aa * aa * aa * aa * aa / 720
                )
            );

            if (lat < 0) northing += 10000000;

            return `${zone}${band} ${Math.round(easting)} E / ${Math.round(northing)} N`;
        } catch (err) {
            return 'N/A';
        }
    }
};
