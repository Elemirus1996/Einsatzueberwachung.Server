namespace Einsatzueberwachung.Domain.Interfaces;

/// <summary>
/// Verwaltet die Token-basierte Authentifizierung der mobilen Team-Ansicht.
/// Master-Token gilt pro Einsatz, Team-Cookies werden bei Einsatz-Ende automatisch entwertet.
/// </summary>
public interface ITeamMobileTokenService
{
    /// <summary>Aktuell gültiger Master-Token (null wenn kein aktiver Einsatz).</summary>
    string? CurrentMasterToken { get; }

    /// <summary>Aktuelle Generation – fest je Einsatz, wechselt bei Einsatz-Start und -Ende und entwertet dann alle Team-Cookies.</summary>
    int CurrentGeneration { get; }

    /// <summary>Wird gefeuert wenn die Generation wechselt (Einsatz-Start oder -Ende).</summary>
    event Action? GenerationChanged;

    /// <summary>Validiert einen Master-Token (konstantzeitlicher Vergleich).</summary>
    bool ValidateMasterToken(string token);

    /// <summary>Erzeugt einen signierten Cookie-Wert für ein Team.</summary>
    string IssueTeamCookieValue(string teamId);

    /// <summary>Validiert einen Team-Cookie-Wert. Liefert die TeamId zurück wenn gültig.</summary>
    bool TryValidateTeamCookie(string cookieValue, out string teamId);

    /// <summary>Wird gefeuert wenn der Einsatzleiter eine Nachricht an ein bestimmtes Team schickt.</summary>
    event Action<string, string, DateTime>? TeamMessageBroadcasted;

    /// <summary>Broadcast einer Nachricht an ein Team (vom Einsatzleiter).</summary>
    void BroadcastTeamMessage(string teamId, string message);
}
