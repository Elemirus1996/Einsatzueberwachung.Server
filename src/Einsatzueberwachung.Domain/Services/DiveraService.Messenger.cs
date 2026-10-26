using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Fotos der Einsatzleitung in den Messenger-Chat des Divera-Einsatzes (Zusatzmodule „Messenger“ und „Cloud“).
    /// Divera legt je Alarm einen Chat an; die Kanal-ID steht nicht in der offiziellen Doku – sie wird deshalb in den
    /// Alarmdaten gesucht (Feld mit „message_channel“ bzw. „channel“ im Namen). Die Kanal-Pruefung in den
    /// Einstellungen zeigt, was Divera fuer die eigene Einheit tatsaechlich liefert.
    /// </summary>
    public partial class DiveraService
    {
        public const long MaxFotoBytes = 10 * 1024 * 1024;

        public async Task<DiveraFotoErgebnis> SendeFotoAnEinsatzChatAsync(int alarmId, byte[] bild, string dateiname, string contentType, string? text)
        {
            await LoadConfigIfNeededAsync();
            if (!FotosSenden)
                return DiveraFotoErgebnis.Fehler("Fotos an Divera sind in den Einstellungen nicht eingeschaltet.");
            if (bild.Length == 0 || bild.Length > MaxFotoBytes)
                return DiveraFotoErgebnis.Fehler("Foto ist leer oder größer als 10 MB.");

            try
            {
                var kanalId = await FindeEinsatzKanalAsync(alarmId);
                if (kanalId is null)
                    return DiveraFotoErgebnis.Fehler("Zu diesem Divera-Einsatz wurde kein Messenger-Chat gefunden (Kanal-Prüfung in den Einstellungen).");

                // 1. Nachricht im Kanal anlegen
                var nachricht = new
                {
                    Message = new
                    {
                        message_channel_id = kanalId.Value,
                        text = string.IsNullOrWhiteSpace(text) ? "Foto der Einsatzleitung" : text.Trim()
                    }
                };
                using var nachrichtResponse = await _httpClient.PostAsync(
                    $"{_baseUrl}/messages?accesskey={_accessKey}",
                    new StringContent(JsonSerializer.Serialize(nachricht), Encoding.UTF8, "application/json"));
                var nachrichtJson = await nachrichtResponse.Content.ReadAsStringAsync();
                var nachrichtId = nachrichtResponse.IsSuccessStatusCode ? LeseDatenId(nachrichtJson) : null;
                if (nachrichtId is null)
                {
                    _logger.LogWarning("Divera: Nachricht konnte nicht angelegt werden (HTTP {Status})", (int)nachrichtResponse.StatusCode);
                    return DiveraFotoErgebnis.Fehler($"Divera hat die Nachricht abgelehnt (HTTP {(int)nachrichtResponse.StatusCode}). Rechte des Schlüssels im Messenger prüfen.");
                }

                // 2. Foto als Anhang an die Nachricht
                using var form = new MultipartFormDataContent();
                var datei = new ByteArrayContent(bild);
                datei.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                form.Add(datei, "Attachment[upload]", string.IsNullOrWhiteSpace(dateiname) ? "foto.jpg" : dateiname);
                form.Add(new StringContent("Foto"), "Attachment[title]");
                form.Add(new StringContent(string.IsNullOrWhiteSpace(text) ? "Foto der Einsatzleitung" : text.Trim()), "Attachment[description]");

                using var anhangResponse = await _httpClient.PostAsync($"{_baseUrl}/messages/attachment/{nachrichtId}?accesskey={_accessKey}", form);
                if (!anhangResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Divera: Foto-Anhang abgelehnt (HTTP {Status})", (int)anhangResponse.StatusCode);
                    return DiveraFotoErgebnis.Fehler($"Nachricht angelegt, Foto aber abgelehnt (HTTP {(int)anhangResponse.StatusCode}). Sind „Messenger“ und „Cloud“ gebucht?");
                }

                _logger.LogInformation("Divera: Foto an Einsatz-Chat {Kanal} gesendet (Alarm {Alarm})", kanalId, alarmId);
                return DiveraFotoErgebnis.Ok();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Divera: Foto konnte nicht gesendet werden");
                return DiveraFotoErgebnis.Fehler("Divera nicht erreichbar – Foto nicht gesendet.");
            }
        }

        private async Task<int?> FindeEinsatzKanalAsync(int alarmId)
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}/alarms/{alarmId}?accesskey={_accessKey}");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("data", out var data) ? FindeKanalId(data) : null;
        }

        /// <summary>Sucht in den Alarmdaten ein Feld wie „message_channel_id“ (bevorzugt) oder „…channel…“ mit einer Zahl.</summary>
        public static int? FindeKanalId(JsonElement element)
        {
            int? Suche(JsonElement e, Func<string, bool> passt, int tiefe)
            {
                if (tiefe > 4) return null;
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in e.EnumerateObject())
                    {
                        if (passt(p.Name) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var id) && id > 0)
                            return id;
                    }
                    foreach (var p in e.EnumerateObject())
                    {
                        var tiefer = Suche(p.Value, passt, tiefe + 1);
                        if (tiefer is not null) return tiefer;
                    }
                }
                return null;
            }

            return Suche(element, n => n.Contains("message_channel", StringComparison.OrdinalIgnoreCase), 0)
                ?? Suche(element, n => n.Contains("channel", StringComparison.OrdinalIgnoreCase) && n.EndsWith("id", StringComparison.OrdinalIgnoreCase), 0);
        }

        private static int? LeseDatenId(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("id", out var id)
                    && id.TryGetInt32(out var value) ? value : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Kanal-Pruefung (Einstellungen → Divera): zeigt nur Feldnamen, IDs und Kanal-Zuordnungen – keine Inhalte –,
        /// damit man sieht, wie Divera den Chat eines Einsatzes fuer die eigene Einheit liefert.
        /// </summary>
        public async Task<IReadOnlyList<string>> PruefeEinsatzChatAsync()
        {
            await LoadConfigIfNeededAsync();
            var zeilen = new List<string>();
            if (!IsConfigured)
            {
                zeilen.Add("Divera ist nicht eingerichtet (Schalter oder API-Key fehlt).");
                return zeilen;
            }

            try
            {
                var alarme = await GetActiveAlarmsAsync();
                var alarm = alarme.OrderByDescending(a => a.Date).FirstOrDefault();
                if (alarm is null)
                {
                    zeilen.Add("Kein offener Divera-Alarm – für die Prüfung einen (Test-)Alarm in Divera auslösen.");
                }
                else
                {
                    zeilen.Add($"Alarm #{alarm.Id} („{alarm.Title}“):");
                    using var response = await _httpClient.GetAsync($"{_baseUrl}/alarms/{alarm.Id}?accesskey={_accessKey}");
                    zeilen.Add($"  GET /alarms/{alarm.Id}: HTTP {(int)response.StatusCode}");
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                        {
                            zeilen.Add("  Felder: " + string.Join(", ", data.EnumerateObject().Select(p => p.Name)));
                            var kanal = FindeKanalId(data);
                            zeilen.Add(kanal is null
                                ? "  ✗ Kein Kanal-Feld im Alarm gefunden."
                                : $"  ✓ Einsatz-Chat gefunden: Kanal {kanal}");
                        }
                    }
                }

                using var nachrichten = await _httpClient.GetAsync($"{_baseUrl}/messages?accesskey={_accessKey}");
                zeilen.Add($"GET /messages: HTTP {(int)nachrichten.StatusCode}");
                if (nachrichten.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await nachrichten.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        var items = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList()
                            : data.ValueKind == JsonValueKind.Object ? data.EnumerateObject().Select(p => p.Value).Where(v => v.ValueKind == JsonValueKind.Object).ToList()
                            : new List<JsonElement>();
                        zeilen.Add($"  {items.Count} Nachrichten, Felder: " + (items.Count > 0 && items[0].ValueKind == JsonValueKind.Object
                            ? string.Join(", ", items[0].EnumerateObject().Select(p => p.Name)) : "–"));
                        var kanaele = items
                            .Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("message_channel_id", out _))
                            .Select(i => i.GetProperty("message_channel_id").ToString())
                            .Distinct().ToList();
                        if (kanaele.Count > 0) zeilen.Add("  Kanäle in den Nachrichten: " + string.Join(", ", kanaele));
                    }
                }
            }
            catch (Exception ex)
            {
                zeilen.Add("Fehler: " + ex.Message);
            }

            return zeilen;
        }
    }

}
