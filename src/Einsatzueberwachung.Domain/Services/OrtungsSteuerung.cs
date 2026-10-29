using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>Was die Tracking-App als Naechstes tun soll (Antwort auf jeden Upload, siehe docs/APP_API.md).</summary>
    public enum OrtungsModus
    {
        /// <summary>GPS an, Positionen puffern und hochladen</summary>
        Track,
        /// <summary>GPS aus, nur gelegentlich den Status abfragen</summary>
        Pause,
        /// <summary>GPS aus, Zugang loeschen, Rolle beenden</summary>
        Stop
    }

    /// <summary>
    /// Server steuert die Ortung der App (#110): Nur waehrend einer laufenden Suche (auch in der Kurzpause) wird geortet,
    /// nach dem Einsatz nie.
    /// </summary>
    public static class OrtungsSteuerung
    {
        public static OrtungsModus FuerTeam(EinsatzData? einsatz, Team? team)
        {
            if (einsatz is null || einsatz.EinsatzEnde is not null || team is null)
                return OrtungsModus.Stop;

            // Kurzpause (#133): Team ist weiter im Gelaende – Ortung bleibt an, die Punkte kommen aber nicht in den Laufweg
            return team.IsRunning || team.IsManuallyPaused ? OrtungsModus.Track : OrtungsModus.Pause;
        }

        public static string AlsText(OrtungsModus modus) => modus switch
        {
            OrtungsModus.Track => "track",
            OrtungsModus.Pause => "pause",
            _ => "stop"
        };
    }
}
