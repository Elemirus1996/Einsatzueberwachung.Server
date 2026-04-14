# Feature: Real-time GPS Collar Tracking for Rescue Teams

## 1. Context & Goal
We have an existing ASP.NET Razor WebApp used for organizing rescue dog squad operations. Currently, rescue teams are assigned specific search areas (polygons) defined on the map.
**Goal:** We need to integrate live GPS collar data (up to 20 collars) from an external central software via a REST API, assign those collars to specific rescue teams, display their live path on the map, and trigger an alert if a dog leaves its assigned search area.

## 2. Data Models

| Requirement | Status | File |
|---|---|---|
| `Collar` model (`Id`, `CollarName`, `IsAssigned`, `AssignedTeamId`) | Done | `src/Einsatzueberwachung.Domain/Models/Collar.cs` |
| `CollarLocation` model (`CollarId`, `Latitude`, `Longitude`, `Timestamp`) | Done | `src/Einsatzueberwachung.Domain/Models/CollarLocation.cs` |
| `Team` → `CollarId` + `CollarName` properties | Done | `src/Einsatzueberwachung.Domain/Models/Team.cs` |

Relationship: 1-to-1 — each `Team` can have at most one `Collar` assigned.

## 3. Backend

| Requirement | Status | File |
|---|---|---|
| REST API endpoint (`POST /api/CollarWebhook/location`) | Done | `src/Einsatzueberwachung.Server/Controllers/CollarWebhookController.cs` |
| `GET /api/CollarWebhook/collars` (list all) | Done | same |
| `GET /api/CollarWebhook/collars/available` (unassigned) | Done | same |
| `GET /api/CollarWebhook/history/{collarId}` (breadcrumbs) | Done | same |
| `ICollarTrackingService` interface | Done | `src/Einsatzueberwachung.Domain/Interfaces/ICollarTrackingService.cs` |
| `CollarTrackingService` (thread-safe singleton) | Done | `src/Einsatzueberwachung.Domain/Services/CollarTrackingService.cs` |
| Assignment logic in team editor UI | Done | `EinsatzMonitor.razor` team modal |
| SignalR relay (`collar.location`, `collar.outofbounds`) | Done | `src/Einsatzueberwachung.Server/Services/CollarTrackingRelayService.cs` |
| EinsatzHub collar methods | Done | `src/Einsatzueberwachung.Server/Hubs/EinsatzHub.cs` |
| DI registration (singleton + hosted service) | Done | `src/Einsatzueberwachung.Server/Program.cs` |

Concurrency: `ConcurrentDictionary` for collar registry, lock-guarded history lists. Handles up to 20 simultaneous collars.

## 4. Frontend & Map UI

| Requirement | Status | File |
|---|---|---|
| Collar-to-Team dropdown (format: `CollarName [CollarId]`) | Done | `EinsatzMonitor.razor` team modal (dog teams only) |
| Live polyline drawing on map | Done | `src/Einsatzueberwachung.Server/wwwroot/js/collar-tracking.js` |
| GPS toggle button on map header | Done | `EinsatzKarte.razor` |
| Floating tracking panel (read-only collar list) | Done | `EinsatzKarte.razor` |
| Geospatial bounds check (Ray-Casting) | Done | `CollarTrackingService.IsPointInPolygon()` |
| Out-of-bounds warning (pulsing circle + UI alert) | Done | `collar-tracking.js` + `CollarTrackingRelayService` |
| Scoped CSS for tracking panel | Done | `EinsatzKarte.razor.css` |

## 5. Constraints & Rules
* Clean, modular JS in `collar-tracking.js` — separated from `leaflet-interop.js`.
* `ConcurrentDictionary` ensures thread-safe concurrent collar updates.
* Follows existing DI patterns (singleton service, hosted relay service).
* No database migration needed — collar data is in-memory only (cleared on restart or mission end via `ClearAll()`). Historical breadcrumbs are transient by design since they are only relevant during an active mission.

## 6. Architecture Overview

```
External Collar Software
        |
        v
  POST /api/CollarWebhook/location
        |
        v
  CollarTrackingService (in-memory, thread-safe)
        |
        +---> CollarLocationReceived event
        |         |
        |         v
        |   CollarTrackingRelayService (hosted service)
        |         |
        |         v
        |   SignalR broadcast "collar.location"
        |         |
        |         v
        |   EinsatzKarte.razor (Blazor domain event handler)
        |         |
        |         v
        |   collar-tracking.js → Leaflet polyline + marker
        |
        +---> OutOfBoundsDetected event (if outside polygon)
                  |
                  v
            SignalR broadcast "collar.outofbounds"
                  |
                  v
            Red pulsing circle on map + UI warning
```