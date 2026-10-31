using System;
using System.Collections.Generic;
using System.Linq;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Loeschfristen je Staffel (#151): Jede Datenart im Archiv wird nach einer einstellbaren Zahl von Tagen ab Einsatzende
    /// geloescht. Ohne Frist (null) wird nie automatisch geloescht. Uebungen koennen eine eigene, gemeinsame Frist haben.
    /// </summary>
    public static class Loeschfristen
    {
        public const string Laufwege = "laufwege";
        public const string Notizen = "notizen";
        public const string Vermisste = "vermisste";

        public static readonly IReadOnlyList<string> Arten = [Laufwege, Notizen, Vermisste];

        public const int MaxTage = 3650;

        public static string Anzeige(string art) => art switch
        {
            Laufwege => "Laufwege",
            Notizen => "Notizen, Statusmeldungen und Funk",
            _ => "Angaben zur vermissten Person"
        };

        /// <summary>Frist in Tagen fuer diese Art; bei Uebungen gilt die Uebungsfrist, wenn gesetzt</summary>
        public static int? Frist(AppSettings s, string art, bool uebung)
        {
            if (uebung && s.LoeschfristUebungenTage is { } tage) return tage;
            return art switch
            {
                Laufwege => s.LoeschfristLaufwegeTage,
                Notizen => s.LoeschfristNotizenTage,
                _ => s.LoeschfristVermissteTage
            };
        }

        public static DateTime Bezug(ArchivedEinsatz a) => a.EinsatzEnde ?? a.EinsatzDatum;

        /// <summary>Arten, deren Frist abgelaufen ist und die noch nicht geloescht wurden</summary>
        public static List<string> Faellig(ArchivedEinsatz a, AppSettings s, DateTime jetzt) =>
            Arten.Where(art => a.Geloescht?.ContainsKey(art) != true
                && Frist(s, art, !a.IstEinsatz) is { } tage
                && Bezug(a).AddDays(tage) <= jetzt).ToList();

        /// <summary>Daten der Arten aus dem archivierten Einsatz entfernen und vermerken, wann</summary>
        public static void Anwenden(ArchivedEinsatz a, IEnumerable<string> arten, DateTime jetzt)
        {
            a.Geloescht ??= new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var art in arten)
            {
                switch (art)
                {
                    case Laufwege:
                        a.TrackSnapshots = [];
                        foreach (var suche in a.CompletedSearches ?? []) suche.Tracks = [];
                        foreach (var team in a.Teams ?? []) team.TrackSnapshots = [];
                        break;
                    case Notizen:
                        a.GlobalNotesEntries = [];
                        break;
                    case Vermisste:
                        a.Vermisste = [];
                        break;
                }
                a.Geloescht[art] = jetzt;
            }
        }

        /// <summary>Text fuer die Datenschutzerklaerung ({frist.laufwege} usw.)</summary>
        public static string Text(int? tage) => tage is { } t
            ? $"{t} Tage nach Ende des Einsatzes, danach automatisch gelöscht"
            : "bis zur Löschung durch die Staffel (keine automatische Löschfrist)";
    }
}
