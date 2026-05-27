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
        // crossOrigin: 'anonymous' erlaubt Canvas-Export für CORS-kompatible Tile-Server
        const osmLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '(c) OpenStreetMap contributors',
            maxZoom: 19,
            crossOrigin: 'anonymous'
        });

        // Esri Satellite Layer (CORS-kompatibel)
        const satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
            attribution: 'Tiles (c) Esri',
            maxZoom: 18,
            crossOrigin: 'anonymous'
        });

        // Google Satellite (kein CORS – Kacheln erscheinen im Screenshot als grauer Hintergrund)
        const googleSatellite = L.tileLayer('https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}', {
            attribution: 'Map data (c) Google',
            maxZoom: 20
        });

        // Hybrid: Satellit mit Strassennamen (kein CORS)
        const hybridLayer = L.tileLayer('https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}', {
            attribution: 'Map data (c) Google',
            maxZoom: 20
        });

        // OpenTopoMap: Topografische Karte mit Hoehenlinien (CORS-kompatibel)
        const topoLayer = L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png', {
            maxZoom: 17,
            attribution: 'Kartendaten: (c) OpenStreetMap | Kartenstil: (c) <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)',
            crossOrigin: 'anonymous'
        });
        
        log('Layer erstellt');
        
        // Standard-Layer hinzufuegen
        osmLayer.addTo(map);
        
        // Layer Control mit besserer Konfiguration
        const baseMaps = {
            "Strassenkarte": osmLayer,
            "Satellit (Esri)": satelliteLayer,
            "Satellit (Google)": googleSatellite,
            "Hybrid (Google)": hybridLayer,
            "Topografisch": topoLayer
        };
        
        log('Layer Control wird hinzugefuegt');
        const layerControl = L.control.layers(baseMaps, null, {
            position: 'topright',
            collapsed: false  // Immer ausgeklappt fuer bessere Sichtbarkeit
        });
        layerControl.addTo(map);
        log('Layer Control hinzugeFuegt:', layerControl);
        
        // Koordinaten-Gitter Control (UTM und Lat/Lon)
        // UTM-Zone automatisch anhand der Kartenmitte bestimmen
        const utmZone = Math.floor((centerLng + 180) / 6) + 1;
        const bSouth = centerLat < 0;
        
        // UTM-Gitter als LayerGroup (mehrere benachbarte Zonen fuer bessere Abdeckung)
        const utmGridGroup = L.layerGroup();
        // Hauptzone und ggf. benachbarte Zonen hinzufuegen
        for (let z = Math.max(1, utmZone - 1); z <= Math.min(60, utmZone + 1); z++) {
            // Jede UTM-Zone auf ihre 6-Grad-Laengenband-Grenzen clippen
            const zoneLngWest = (z - 1) * 6 - 180;
            const zoneLngEast = z * 6 - 180;
            const utmGrid = L.utmGrid(z, bSouth, {
                color: "rgba(0, 100, 255, 0.6)",
                weight: 1.5,
                opacity: 0.8,
                font: "bold 11px Verdana",
                minZoom: 6,
                showAxisLabels: [100, 500, 1000, 5000, 10000, 50000, 100000],
                showAxis100km: true,
                showSquareLabels: [100000],
                latLonClipBounds: [[-85, zoneLngWest], [85, zoneLngEast]]
            });
            utmGridGroup.addLayer(utmGrid);
        }
        
        // Lat/Lon Dezimal-Gitter
        const latLonGrid = L.latLonGrid({
            color: "rgba(214, 51, 132, 0.6)",
            weight: 1.5,
            opacity: 0.8,
            font: "bold 11px Verdana",
            minZoom: 3
        });
        
        // Grid Selector Control hinzufuegen
        const gridControl = L.control.gridSelector({
            "Ohne": null,
            "UTM": utmGridGroup,
            "Lat/Lon (dezimal)": latLonGrid
        }, {
            position: 'topright'
        });
        gridControl.addTo(map);
        log('Grid Control hinzugefuegt');

        // Maßstab (metrisch)
        L.control.scale({ imperial: false, position: 'bottomleft' }).addTo(map);
            
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
        let drawShapeCreated = false; // Flag: wurde draw durch vollständiges Zeichnen beendet?
        map.on(L.Draw.Event.CREATED, function(e) {
            const layer = e.layer;
            drawnItems.addLayer(layer);
            drawShapeCreated = true; // Form wurde erstellt, kein Cancel
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
            gridLayers: {
                none: null,
                utm: utmGridGroup,
                latlon: latLonGrid
            },
            currentGridLayer: null,
            currentGridLayerType: 'none',
            searchAreasVisible: true,
            coordinateMarkersVisible: true,
            layers: {
                streets: osmLayer,
                satellite: satelliteLayer,
                satelliteGoogle: googleSatellite,
                hybrid: hybridLayer,
                topo: topoLayer
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
        map.on('draw:drawstart', function() { drawModeActive = true; drawShapeCreated = false; });
        map.on('draw:drawstop', function() {
            drawModeActive = false;
            hideLiveArea();
            // Nur bei echtem Abbruch (Escape / cancelDrawMode) → Blazor informieren
            if (!drawShapeCreated && dotNetReference) {
                dotNetReference.invokeMethodAsync('OnDrawCanceled');
            }
            drawShapeCreated = false;
        });

        // Edit beendet (Speichern oder Abbrechen) → ausblenden
        map.on('draw:editstop', function() { hideLiveArea(); });
        // ---- Ende Live-Flächen-Anzeige ----

        // Live-Flächen-Hilfsfunktionen für Nutzung in startPolygonEdit speichern
        this.maps[mapId].updateLiveArea = updateLiveArea;
        this.maps[mapId].hideLiveArea = hideLiveArea;

        return true;
    } catch (err) {
        error('Fehler beim Initialisieren der Karte:', err);
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
        } catch (err) {
            error('Fehler beim Hinzufuegen des Suchgebiets:', err);
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
        } catch (err) {
            error('Fehler beim Entfernen des Suchgebiets:', err);
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
        } catch (err) {
            error('Fehler beim Setzen des Markers:', err);
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
        } catch (err) {
            error('Fehler beim Entfernen des Markers:', err);
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
        } catch (err) {
            error('Fehler beim Zentrieren der Karte:', err);
            return false;
        }
    },

    // Zoomt auf ein bestimmtes Suchgebiet anhand der gespeicherten Layer-ID
    zoomToSearchArea: function(mapId, areaId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData || !areaId) return false;

            const layer = mapData.markers?.[areaId];
            if (!layer || typeof layer.getBounds !== 'function') return false;

            const bounds = layer.getBounds();
            if (!bounds || !bounds.isValid()) return false;

            mapData.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 17 });
            if (typeof layer.openPopup === 'function') {
                layer.openPopup();
            }
            return true;
        } catch (err) {
            console.error('Fehler beim Zoomen auf Suchgebiet:', err);
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
        } catch (err) {
            error('Fehler beim Abrufen der Kartenmitte:', err);
            return { lat: 0, lng: 0 };
        }
    },
    
    // Gibt das aktuelle Viewport (Mitte + Zoom) zurueck
    getMapViewport: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return { lat: 0, lng: 0, zoom: 13 };
            
            const center = mapData.map.getCenter();
            const zoom = mapData.map.getZoom();
            return {
                lat: center.lat,
                lng: center.lng,
                zoom: zoom
            };
        } catch (err) {
            error('Fehler beim Abrufen des Viewports:', err);
            return { lat: 0, lng: 0, zoom: 13 };
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
                case 'satelliteGoogle':
                    newLayer = mapData.layers.satelliteGoogle;
                    break;
                case 'hybrid':
                    newLayer = mapData.layers.hybrid;
                    break;
                case 'topo':
                    newLayer = mapData.layers.topo;
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
        } catch (err) {
            error('Fehler beim Wechseln des Layers:', err);
            return false;
        }
    },

    changeGridLayer: function(mapId, layerType) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }

            if (mapData.currentGridLayer) {
                mapData.map.removeLayer(mapData.currentGridLayer);
            }

            mapData.currentGridLayer = null;
            mapData.currentGridLayerType = layerType || 'none';

            const nextLayer = mapData.gridLayers?.[mapData.currentGridLayerType];
            if (nextLayer) {
                nextLayer.addTo(mapData.map);
                mapData.currentGridLayer = nextLayer;
            }

            return true;
        } catch (err) {
            error('Fehler beim Wechseln des Koordinaten-Gitters:', err);
            return false;
        }
    },

    toggleSearchAreas: function(mapId, visible) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData || !mapData.savedAreas) return false;

            mapData.searchAreasVisible = !!visible;

            if (mapData.searchAreasVisible) {
                if (!mapData.map.hasLayer(mapData.savedAreas)) {
                    mapData.savedAreas.addTo(mapData.map);
                }
            } else if (mapData.map.hasLayer(mapData.savedAreas)) {
                mapData.map.removeLayer(mapData.savedAreas);
            }

            return true;
        } catch (err) {
            error('Fehler beim Umschalten der Suchgebiete:', err);
            return false;
        }
    },

    toggleCoordinateMarkers: function(mapId, visible) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;

            mapData.coordinateMarkersVisible = !!visible;

            Object.keys(mapData.markers)
                .filter(key => key.startsWith('coord_'))
                .forEach(key => {
                    const marker = mapData.markers[key];
                    if (!marker) {
                        return;
                    }

                    if (mapData.coordinateMarkersVisible) {
                        if (!mapData.map.hasLayer(marker)) {
                            marker.addTo(mapData.map);
                        }
                    } else if (mapData.map.hasLayer(marker)) {
                        mapData.map.removeLayer(marker);
                    }
                });

            return true;
        } catch (err) {
            error('Fehler beim Umschalten der Punkte:', err);
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
        } catch (err) {
            error('Fehler beim Anpassen der Bounds:', err);
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
        } catch (err) {
            error('Fehler beim Geocoding:', err);
            return { success: false, message: err.message };
        }
    },
    
    // ── DRUCK: Screenshot-basierter Ansatz ─────────────────────────────────────
    // Öffnet ein dediziertes Druckfenster mit:
    //   Seite 1 – Karten-Screenshot (A4 Querformat)
    //   Seite 2 – Suchgebiete-Liste  (A4 Hochformat)
    // ────────────────────────────────────────────────────────────────────────────

    printMap: async function(mapId, layerName) {
        log('printMap aufgerufen für:', mapId, 'Layer:', layerName);
        try {
            const mapData = this.maps[mapId];
            if (!mapData) { error('Karte nicht gefunden:', mapId); return false; }

            const map = mapData.map;

            // Gewünschten Basis-Layer aktivieren
            const allBaseLayers = Object.values(mapData.layers).filter(Boolean);
            const originalBaseLayer = allBaseLayers.find(l => map.hasLayer(l)) || null;
            const requestedBaseLayer = layerName && mapData.layers[layerName]
                ? mapData.layers[layerName] : null;

            if (requestedBaseLayer && requestedBaseLayer !== originalBaseLayer) {
                allBaseLayers.forEach(l => { if (map.hasLayer(l)) map.removeLayer(l); });
                map.addLayer(requestedBaseLayer);
            }

            // Kacheln laden lassen
            await new Promise(resolve => setTimeout(resolve, 1800));

            // Screenshot
            const imageDataUrl = await window.LeafletMap._captureMapCanvas(map);

            // Basis-Layer wiederherstellen
            if (requestedBaseLayer && requestedBaseLayer !== originalBaseLayer) {
                allBaseLayers.forEach(l => { if (map.hasLayer(l)) map.removeLayer(l); });
                if (originalBaseLayer) map.addLayer(originalBaseLayer);
            }

            window.LeafletMap._openPrintWindow(imageDataUrl);
            return true;
        } catch (err) {
            console.error('Fehler beim Drucken der Karte:', err);
            return false;
        }
    },

    // Zeichnet die aktuelle Kartenansicht auf einen Canvas und gibt dataURL zurück
    _captureMapCanvas: async function(map) {
        const container = map.getContainer();
        const size = map.getSize();
        const cr = container.getBoundingClientRect(); // container rect

        const canvas = document.createElement('canvas');
        canvas.width  = size.x;
        canvas.height = size.y;
        const ctx = canvas.getContext('2d');

        // Hintergrund (sichtbar wenn Kacheln fehlen / CORS-gesperrt)
        ctx.fillStyle = '#e8e8e8';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        // ── 1. Kacheln (Tile-Bilder) ──────────────────────────────────────────
        const tilePane = container.querySelector('.leaflet-tile-pane');
        if (tilePane) {
            for (const tile of tilePane.querySelectorAll('img.leaflet-tile')) {
                if (!tile.complete || tile.naturalWidth === 0) continue;
                const r = tile.getBoundingClientRect();
                try {
                    ctx.drawImage(tile,
                        Math.round(r.left - cr.left), Math.round(r.top - cr.top),
                        Math.round(r.width),          Math.round(r.height));
                } catch (e) { /* CORS-gesperrt (Google-Tiles) → überspringen */ }
            }
        }

        // ── 2. SVG-Overlays (Suchgebiet-Polygone, GPS-Pfade) ─────────────────
        const overlayPane = container.querySelector('.leaflet-overlay-pane');
        if (overlayPane) {
            for (const svg of overlayPane.querySelectorAll('svg')) {
                try {
                    const r = svg.getBoundingClientRect();
                    if (r.width === 0 || r.height === 0) continue;
                    const clone = svg.cloneNode(true);
                    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
                    clone.setAttribute('width',  r.width);
                    clone.setAttribute('height', r.height);
                    if (!clone.getAttribute('viewBox'))
                        clone.setAttribute('viewBox', `0 0 ${r.width} ${r.height}`);
                    const blob = new Blob(
                        [new XMLSerializer().serializeToString(clone)],
                        { type: 'image/svg+xml;charset=utf-8' }
                    );
                    const url = URL.createObjectURL(blob);
                    await new Promise(resolve => {
                        const img = new Image();
                        img.onload = () => {
                            ctx.drawImage(img,
                                Math.round(r.left - cr.left), Math.round(r.top - cr.top),
                                Math.round(r.width),          Math.round(r.height));
                            URL.revokeObjectURL(url);
                            resolve();
                        };
                        img.onerror = () => { URL.revokeObjectURL(url); resolve(); };
                        img.src = url;
                    });
                } catch (e) { log('SVG-Capture-Fehler:', e); }
            }
        }

        // ── 3. Marker (ELW-divIcon, Koordinaten-Marker) ──────────────────────
        const markerPane = container.querySelector('.leaflet-marker-pane');
        if (markerPane) {
            for (const markerEl of markerPane.querySelectorAll('.leaflet-marker-icon')) {
                const r = markerEl.getBoundingClientRect();
                const x = Math.round(r.left - cr.left);
                const y = Math.round(r.top  - cr.top);
                const w = Math.round(r.width)  || 32;
                const h = Math.round(r.height) || 32;

                const svgEl = markerEl.querySelector('svg');
                if (svgEl) {
                    // divIcon mit eingebettetem SVG (z. B. ELW-Icon, Koordinaten-Marker)
                    try {
                        const clone = svgEl.cloneNode(true);
                        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
                        const blob = new Blob(
                            [new XMLSerializer().serializeToString(clone)],
                            { type: 'image/svg+xml;charset=utf-8' }
                        );
                        const url = URL.createObjectURL(blob);
                        await new Promise(resolve => {
                            const img = new Image();
                            img.onload = () => {
                                ctx.drawImage(img, x, y, w, h);
                                URL.revokeObjectURL(url);
                                resolve();
                            };
                            img.onerror = () => { URL.revokeObjectURL(url); resolve(); };
                            img.src = url;
                        });
                    } catch (e) {}
                } else if (markerEl.tagName === 'IMG' && markerEl.complete && markerEl.naturalWidth > 0) {
                    // Standard Leaflet img-Marker (Einsatzort etc.)
                    try { ctx.drawImage(markerEl, x, y, w, h); } catch (e) {}
                }
            }
        }

        return canvas.toDataURL('image/png', 0.93);
    },

    // Druckt via window.print() auf dem Hauptfenster – funktioniert in Chrome, Firefox und Safari
    // (window.open und iframe.print sind in Blazor Server durch Pop-up-Blocker/Safari-Bugs unzuverlässig)
    _openPrintWindow: function(imageDataUrl) {
        const listTemplate = document.querySelector('.print-list-template');
        const listHtml = listTemplate
            ? listTemplate.innerHTML
            : '<p style="color:#666;font-style:italic">Keine Suchgebiete vorhanden.</p>';

        // Alte Elemente aufräumen
        ['_print-overlay', '_print-style'].forEach(function(id) {
            const el = document.getElementById(id);
            if (el) el.remove();
        });

        // Print-Styles in das Hauptdokument injizieren
        const style = document.createElement('style');
        style.id = '_print-style';
        style.textContent = [
            '@page { size: A4 landscape; margin: 0.8cm; }',
            '@media print {',
            '  body > *:not(#_print-overlay) { display: none !important; }',
            '  #_print-overlay { display: block !important; }',
            '  #_print-overlay { font-family: Arial, sans-serif; }',
            '  .print-page-map {',
            '    page-break-after: always; break-after: page;',
            '    width: 100%; height: 100vh;',
            '    display: flex; align-items: center; justify-content: center;',
            '  }',
            '  .print-page-map img { max-width:100%; max-height:100%; display:block; object-fit:contain; }',
            '  .print-list-header {',
            '    padding:14px 18px; margin-bottom:16px; background:#1d4e89; border-radius:6px;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '  .print-list-header h2 { margin:0 0 4px; font-size:15pt; color:#fff; }',
            '  .print-list-header p  { margin:3px 0 0; font-size:10pt; color:rgba(255,255,255,.88); }',
            '  .print-list-header strong { color:#fff; }',
            '  .search-areas-table { width:100%; border-collapse:collapse; font-size:10pt; margin-bottom:16px; }',
            '  .search-areas-table thead th {',
            '    background:#1d4e89; color:#fff; padding:9px 8px;',
            '    text-align:left; font-weight:bold; border:1px solid #1d4e89;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '  .search-areas-table tbody tr:nth-child(even) { background:#f4f8fd; -webkit-print-color-adjust:exact; print-color-adjust:exact; }',
            '  .search-areas-table td { padding:9px 8px; border:1px solid #ddd; vertical-align:middle; }',
            '  .color-box {',
            '    display:inline-block; width:16px; height:16px;',
            '    border:2px solid rgba(0,0,0,.25); border-radius:3px; margin-right:7px; vertical-align:middle;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '  .team-name {',
            '    display:inline-block; background:#1d4e89; color:#fff;',
            '    padding:2px 7px; border-radius:3px; font-size:9pt;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '  .status-completed {',
            '    display:inline-block; background:#198754; color:#fff;',
            '    padding:2px 7px; border-radius:3px; font-size:8.5pt; font-weight:bold;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '  .status-in-progress {',
            '    display:inline-block; background:#e67e22; color:#fff;',
            '    padding:2px 7px; border-radius:3px; font-size:8.5pt; font-weight:bold;',
            '    -webkit-print-color-adjust:exact; print-color-adjust:exact;',
            '  }',
            '}',
            '#_print-overlay { display:none; }'
        ].join('\n');
        document.head.appendChild(style);

        // Print-Overlay in den Body einfügen
        const overlay = document.createElement('div');
        overlay.id = '_print-overlay';
        overlay.innerHTML =
            '<div class="print-page-map"><img src="' + imageDataUrl + '" alt="Einsatzkarte"></div>' +
            '<div class="print-page-list">' + listHtml + '</div>';
        document.body.appendChild(overlay);

        // Drucken und danach aufräumen
        function cleanup() {
            document.getElementById('_print-overlay')?.remove();
            document.getElementById('_print-style')?.remove();
        }
        window.addEventListener('afterprint', cleanup, { once: true });
        setTimeout(cleanup, 120000); // Fallback nach 2 Minuten

        window.print();
    },
    
    // Exportiert die Karte als Bild (PNG)
    exportMapImage: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            
            // Hinweis fuer Benutzer
            alert('Tipp: Nutzen Sie "Karte drucken" und speichern Sie als PDF, oder machen Sie einen Screenshot (Windows: Win+Shift+S, Mac: Cmd+Shift+4)');
            return true;
        } catch (err) {
            error('Fehler beim Exportieren:', err);
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
        } catch (err) {
            error('Fehler beim Dispose der Karte:', err);
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

    // Bricht den aktiven Zeichen-Modus ab (vom X-Button oder programmatisch)
    cancelDrawMode: function(mapId) {
        try {
            const mapData = this.maps[mapId];
            if (!mapData) return false;
            const drawMode = mapData.drawControl._toolbars && mapData.drawControl._toolbars.draw;
            if (drawMode) {
                if (drawMode._modes.polygon && drawMode._modes.polygon.handler._enabled) {
                    drawMode._modes.polygon.handler.disable();
                }
                if (drawMode._modes.rectangle && drawMode._modes.rectangle.handler._enabled) {
                    drawMode._modes.rectangle.handler.disable();
                }
            }
            return true;
        } catch (err) {
            console.error('Fehler beim Abbrechen des Draw-Modus:', err);
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
            });

            if (mapData.coordinateMarkersVisible !== false) {
                marker.addTo(mapData.map);
            }

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
