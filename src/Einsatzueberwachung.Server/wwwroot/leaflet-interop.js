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
    
    // Druckt die Karte - zentriert vorher auf alle Elemente
    printMap: function(mapId) {
        log('printMap aufgerufen für:', mapId);
        try {
            const mapData = this.maps[mapId];
            if (!mapData) {
                error('Karte nicht gefunden:', mapId);
                return false;
            }
            
            const map = mapData.map;
            
            // Speichere aktuelle Ansicht für später
            const currentCenter = map.getCenter();
            const currentZoom = map.getZoom();
            
            console.log('printMap: Verwende aktuelle Bildschirmansicht - Center:', currentCenter, 'Zoom:', currentZoom);

            // Vor dem Drucken Ansicht explizit auf aktuelle Bildschirmansicht fixieren
            const beforePrintHandler = () => {
                console.log('beforeprint: Setze gespeicherte Ansicht im Print-Layout');
                setTimeout(() => {
                    map.invalidateSize();
                    map.setView(currentCenter, currentZoom, { animate: false });

                    // Zweiter Pass nach Layout-Flush für stabile Position
                    setTimeout(() => {
                        map.invalidateSize();
                        map.setView(currentCenter, currentZoom, { animate: false });
                    }, 180);
                }, 120);
            };
            
            // afterprint Event zum Wiederherstellen
            const afterPrintHandler = () => {
                window.removeEventListener('beforeprint', beforePrintHandler);
                window.removeEventListener('afterprint', afterPrintHandler);
                
                // Ansicht wiederherstellen
                setTimeout(() => {
                    map.invalidateSize();
                    map.setView(currentCenter, currentZoom);
                    console.log('printMap: Ansicht wiederhergestellt');
                }, 200);
            };
            
            window.addEventListener('beforeprint', beforePrintHandler);
            window.addEventListener('afterprint', afterPrintHandler);
            
            // Direkt vor Druck auf die aktuelle Ansicht fixieren
            setTimeout(() => {
                map.invalidateSize();
                map.setView(currentCenter, currentZoom, { animate: false });
                
                setTimeout(() => {
                    map.invalidateSize();
                    
                    // Jetzt drucken
                    setTimeout(() => {
                        log('Starte Druck-Dialog');
                        window.print();
                    }, 300);
                }, 200);
            }, 450);
            
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
            outerLayer.eachLayer(function(l) { innerLayer = l; });

            if (!innerLayer) {
                console.error('Kein innerer Layer gefunden für Area:', areaId);
                mapData.savedAreas.addLayer(outerLayer);
                return false;
            }

            // Metadaten auf innerem Layer sicherstellen
            window.LeafletMap.setSearchAreaMetadata(innerLayer, areaId);

            // In drawnItems verschieben (wird von Leaflet.draw bearbeitet)
            mapData.drawnItems.addLayer(innerLayer);

            // Bearbeitungszustand speichern
            mapData.editingAreaId = areaId;
            mapData.editingOuterLayer = outerLayer;
            mapData.editingInnerLayer = innerLayer;

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
            // save() feuert L.Draw.Event.EDITED und deaktiviert danach den Edit-Modus
            editHandler.save();

            // Inneren Layer aus drawnItems entfernen (OnShapeEdited fügt ihn über addSearchArea neu hinzu)
            if (mapData.editingInnerLayer && mapData.drawnItems.hasLayer(mapData.editingInnerLayer)) {
                mapData.drawnItems.removeLayer(mapData.editingInnerLayer);
            }

            // Bearbeitungszustand zurücksetzen
            mapData.editingAreaId = null;
            mapData.editingOuterLayer = null;
            mapData.editingInnerLayer = null;

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
    }
};
