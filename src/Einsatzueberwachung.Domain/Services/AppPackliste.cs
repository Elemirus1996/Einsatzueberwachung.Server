using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Packliste der Staffel fuer die App (docs/APP_API.md Team-Rolle Abschnitt 14). Die Staffel pflegt sie als Text: eine Zeile
    /// je Punkt, eine Zeile mit „:“ am Ende beginnt eine Gruppe, „- “ am Zeilenanfang wird ignoriert. Die App hakt nur lokal ab,
    /// der Server bekommt dazu nichts zurueck.
    /// </summary>
    public static class AppPackliste
    {
        public const int MaxPunkte = 200;
        public const int MaxTextLaenge = 200;
        public const int MaxGruppeLaenge = 60;
        public const int MaxEingabeLaenge = 20_000;

        public sealed record Punkt(string Id, string Text, string? Gruppe);

        /// <summary>Punkte in der Reihenfolge der Staffel; Id aus Gruppe und Text (8 Zeichen), bei Doppelten mit Zaehler</summary>
        public static IReadOnlyList<Punkt> Punkte(string? eingabe)
        {
            var punkte = new List<Punkt>();
            var vergeben = new HashSet<string>();
            string? gruppe = null;
            foreach (var roh in (eingabe ?? "").Split('\n'))
            {
                var zeile = roh.Trim();
                if (zeile.StartsWith("- ") || zeile.StartsWith("* ")) zeile = zeile[2..].Trim();
                if (zeile.Length == 0) continue;
                if (zeile.EndsWith(':'))
                {
                    var name = zeile.TrimEnd(':').Trim();
                    gruppe = name.Length == 0 ? null : Kuerzen(name, MaxGruppeLaenge);
                    continue;
                }
                var text = Kuerzen(zeile, MaxTextLaenge);
                var basis = Hash((gruppe ?? "") + "\n" + text)[..8];
                var id = basis;
                for (var n = 2; !vergeben.Add(id); n++) id = basis + "-" + n;
                punkte.Add(new Punkt(id, text, gruppe));
                if (punkte.Count == MaxPunkte) break;
            }
            return punkte;
        }

        /// <summary>16 Zeichen Hash der Punkte; null, wenn keine Packliste hinterlegt ist</summary>
        public static string? Version(AppSettings s)
        {
            var punkte = Punkte(s.AppPacklisteText);
            if (punkte.Count == 0) return null;
            return Hash(string.Join("\n", punkte.Select(p => p.Id + "\t" + p.Gruppe + "\t" + p.Text)))[..16];
        }

        /// <summary>Beim Speichern: neue Version und Stand merken, wenn sich die Punkte geaendert haben</summary>
        public static void Aktualisieren(AppSettings s, DateTime nowUtc)
        {
            var version = Version(s);
            if (version == s.AppPacklisteVersion) return;
            s.AppPacklisteVersion = version;
            s.AppPacklisteStandUtc = version is null ? null : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        }

        private static string Kuerzen(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

        private static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
