using System.Net;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Name des Leitungsplatzes anhand der VPN-Adresse (Einstellungen → System → VPN-Geräte), z. B. „Tablet EL“.
/// So steht im Funkstammbuch und im Audit-Log, an welchem Geraet etwas eingegeben wurde, wenn mehrere
/// Leitungsplaetze gleichzeitig arbeiten.
/// </summary>
public sealed class LeitungsGeraetNamen(VpnDeviceStore store)
{
    public const string Einsatzleitung = "Einsatzleitung";

    public string? NameFuer(IPAddress? ip) => GeraetFuer(ip)?.Name;

    /// <summary>Feste Person des VPN-Geraets (APP_API EL Abschnitt 4a); null = keine hinterlegt</summary>
    public string? PersonFuer(IPAddress? ip) => GeraetFuer(ip)?.PersonId;

    private VpnDevice? GeraetFuer(IPAddress? ip)
    {
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var adresse = ip.ToString();
        return store.Devices.FirstOrDefault(d => string.Equals(d.Ip.Split('/')[0].Trim(), adresse, StringComparison.Ordinal));
    }

    /// <summary>„Einsatzleitung“ oder „Einsatzleitung · Tablet EL“</summary>
    public static string Absender(string? geraet) =>
        string.IsNullOrWhiteSpace(geraet) ? Einsatzleitung : $"{Einsatzleitung} · {geraet.Trim()}";
}
