namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorBosContactSummary(
    string? Einheit,
    string? Zugfuehrer,
    string? Funkrufname,
    string? Aufgabenteilung)
{
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Einheit)
        || !string.IsNullOrWhiteSpace(Zugfuehrer)
        || !string.IsNullOrWhiteSpace(Funkrufname)
        || !string.IsNullOrWhiteSpace(Aufgabenteilung);

    public string HeaderLabel => string.IsNullOrWhiteSpace(Einheit) ? "BOS-Kontakt" : Einheit;
}
