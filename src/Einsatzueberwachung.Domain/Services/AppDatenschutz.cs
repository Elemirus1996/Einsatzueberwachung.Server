using System;
using System.Security.Cryptography;
using System.Text;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Datenschutzerklaerung der Staffel fuer die App (#148, docs/APP_API.md Team-Rolle Abschnitt 12): Version und Stand
    /// werden beim Speichern der Einstellungen gesetzt, sobald sich Titel oder Text aendern.
    /// </summary>
    public static class AppDatenschutz
    {
        public const int MaxTextLaenge = 50_000;
        public const int MaxTitelLaenge = 200;

        public static bool Hinterlegt(AppSettings s) => !string.IsNullOrWhiteSpace(s.AppDatenschutzText);

        /// <summary>Text fuer die App: Platzhalter der Loeschfristen (#151) durch die eingestellten Fristen ersetzt</summary>
        public static string Text(AppSettings s) => (s.AppDatenschutzText ?? "").Trim()
            .Replace("{frist.laufwege}", Loeschfristen.Text(s.LoeschfristLaufwegeTage))
            .Replace("{frist.notizen}", Loeschfristen.Text(s.LoeschfristNotizenTage))
            .Replace("{frist.vermisste}", Loeschfristen.Text(s.LoeschfristVermissteTage))
            .Replace("{frist.uebungen}", s.LoeschfristUebungenTage is null ? "wie bei Einsätzen" : Loeschfristen.Text(s.LoeschfristUebungenTage));

        /// <summary>16 Zeichen Hash aus Titel und Text (mit eingesetzten Fristen); null, wenn nichts hinterlegt ist</summary>
        public static string? Version(AppSettings s)
        {
            if (!Hinterlegt(s)) return null;
            var inhalt = (s.AppDatenschutzTitel ?? "").Trim() + "\n" + Text(s);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inhalt)))[..16].ToLowerInvariant();
        }

        /// <summary>Beim Speichern: neue Version und Stand merken, wenn sich der Inhalt geaendert hat</summary>
        public static void Aktualisieren(AppSettings s, DateTime nowUtc)
        {
            var version = Version(s);
            if (version == s.AppDatenschutzVersion) return;
            s.AppDatenschutzVersion = version;
            s.AppDatenschutzStandUtc = version is null ? null : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        }
    }
}
