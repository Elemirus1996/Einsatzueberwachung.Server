using System.Globalization;
using System.Text;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Gemeinsame Logik der Startseiten (grosse Startseite /einsatz-start und EL-Startseite /el/start),
/// damit beide Seiten Einsaetze gleich anlegen.
/// </summary>
public static class EinsatzStartHelper
{
    public static void ApplyStaffel(EinsatzData model, StaffelSettings staffel)
    {
        if (!string.IsNullOrWhiteSpace(staffel.StaffelName)) model.StaffelName = staffel.StaffelName;
        model.StaffelAdresse = staffel.StaffelAdresse;
        model.StaffelTelefon = staffel.StaffelTelefon;
        model.StaffelEmail = staffel.StaffelEmail;
        model.StaffelLogoPfad = staffel.StaffelLogoPfad;
    }

    /// <summary>Divera-Alarm uebernehmen: Ort (mit Koordinate, falls vorhanden), Stichwort, Melder, Nummer, Lagetext, Zeit.</summary>
    /// <returns>true, wenn Koordinaten uebernommen wurden (Kartenadresse dann nicht mehr automatisch angleichen).</returns>
    public static bool ApplyDiveraAlarm(EinsatzData model, DiveraAlarm alarm)
    {
        model.Einsatzort = alarm.Address;
        var hasCoordinates = alarm.Lat.HasValue && alarm.Lng.HasValue;
        if (hasCoordinates)
        {
            ApplyCoordinates(model, alarm.Lat!.Value, alarm.Lng!.Value);
        }
        else
        {
            model.MapAddress = alarm.Address;
        }

        model.Stichwort = alarm.Title;
        model.Alarmiert = alarm.Caller;
        model.EinsatzNummer = !string.IsNullOrWhiteSpace(alarm.ForeignId) ? alarm.ForeignId : $"D-{alarm.Id}";
        model.DiveraAlarmId = alarm.Id;
        model.IstEinsatz = true;

        var lagetext = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(alarm.Text)) lagetext.AppendLine(alarm.Text);
        if (!string.IsNullOrWhiteSpace(alarm.Remark))
        {
            if (lagetext.Length > 0) lagetext.AppendLine();
            lagetext.Append("Hinweis: ").Append(alarm.Remark);
        }
        model.ExportPfad = lagetext.Length > 0 ? lagetext.ToString().Trim() : alarm.Title;

        if (alarm.Date != default) model.AlarmierungsZeit = alarm.Date;
        return hasCoordinates;
    }

    /// <summary>Koordinate als Kartenadresse und ELW-Position (z. B. vom Handy-GPS oder aus Divera).</summary>
    public static void ApplyCoordinates(EinsatzData model, double lat, double lng)
    {
        model.MapAddress = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6}", lat, lng);
        model.ElwPosition = (lat, lng);
    }

    /// <summary>Pflichtangaben pruefen; null = in Ordnung.</summary>
    public static string? Validate(EinsatzData model)
    {
        if (string.IsNullOrWhiteSpace(model.Einsatzort)) return "Bitte Einsatzort ausfüllen.";
        if (model.Szenario == EinsatzSzenarioType.Unbestimmt) return "Bitte ein Szenario auswählen.";
        return null;
    }
}
