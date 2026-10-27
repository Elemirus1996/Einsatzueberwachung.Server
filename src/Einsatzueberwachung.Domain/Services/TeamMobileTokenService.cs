using System.Security.Cryptography;
using System.Text;
using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Domain.Services;

public sealed class TeamMobileTokenService : ITeamMobileTokenService, IDisposable
{
    // URL-safe Alphabet ohne leicht zu verwechselnde Zeichen (0/O, 1/I/l).
    private const string TokenAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MasterTokenLength = 16;

    private readonly byte[] _hmacSecret;
    private readonly IEinsatzService _einsatzService;
    private readonly object _lock = new();

    private string? _currentMasterToken;
    private int _currentGeneration = RandomGeneration();
    private bool _wasActive;
    private DateTime _activeEinsatzDatum;
    private bool _disposed;

    public TeamMobileTokenService(byte[] hmacSecret, IEinsatzService einsatzService)
    {
        if (hmacSecret == null || hmacSecret.Length < 32)
            throw new ArgumentException("HMAC-Secret muss mindestens 32 Bytes lang sein.", nameof(hmacSecret));

        _hmacSecret = hmacSecret;
        _einsatzService = einsatzService ?? throw new ArgumentNullException(nameof(einsatzService));

        _einsatzService.EinsatzChanged += OnEinsatzChanged;

        // Falls Service nach Snapshot-Reload startet und Einsatz schon aktiv ist:
        OnEinsatzChanged();
    }

    public string? CurrentMasterToken
    {
        get { lock (_lock) return _currentMasterToken; }
    }

    public int CurrentGeneration
    {
        get { lock (_lock) return _currentGeneration; }
    }

    public event Action? GenerationChanged;
    public event Action<string, string, DateTime>? TeamMessageBroadcasted;

    public void BroadcastTeamMessage(string teamId, string message)
    {
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(message)) return;
        TeamMessageBroadcasted?.Invoke(teamId, message, Ortszeit.Jetzt);
    }

    public bool ValidateMasterToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        string? current;
        lock (_lock) current = _currentMasterToken;
        if (current == null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(token),
            Encoding.ASCII.GetBytes(current));
    }

    public string IssueTeamCookieValue(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            throw new ArgumentException("teamId fehlt.", nameof(teamId));

        int generation;
        lock (_lock) generation = _currentGeneration;

        var payload = $"{teamId}.{generation}";
        var signature = ComputeSignature(payload);
        return $"{payload}.{signature}";
    }

    public bool TryValidateTeamCookie(string cookieValue, out string teamId)
    {
        teamId = string.Empty;
        if (string.IsNullOrWhiteSpace(cookieValue)) return false;

        var parts = cookieValue.Split('.');
        if (parts.Length != 3) return false;

        var candidateTeamId = parts[0];
        var generationStr = parts[1];
        var signature = parts[2];

        if (string.IsNullOrWhiteSpace(candidateTeamId)) return false;
        if (!int.TryParse(generationStr, out var cookieGeneration)) return false;

        int currentGeneration;
        lock (_lock) currentGeneration = _currentGeneration;
        if (cookieGeneration != currentGeneration) return false;

        var expected = ComputeSignature($"{candidateTeamId}.{cookieGeneration}");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(signature),
                Encoding.ASCII.GetBytes(expected)))
            return false;

        teamId = candidateTeamId;
        return true;
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_hmacSecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        // URL-safe Base64 ohne Padding
        return Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private void OnEinsatzChanged()
    {
        var current = _einsatzService.CurrentEinsatz;
        // Wie EinsatzService: ohne Einsatzort gibt es keinen Einsatz. Das leere Platzhalter-Objekt beim Start hat
        // IstEinsatz = true – ohne diese Pruefung galt es als aktiv, und die Generation hing an seinem Erzeugungszeitpunkt.
        var isActive = current is { IstEinsatz: true, EinsatzEnde: null } && !string.IsNullOrWhiteSpace(current.Einsatzort);

        bool changed = false;
        lock (_lock)
        {
            // Neuer Einsatz ohne Ende des vorigen (anderer Startzeitpunkt): ebenfalls neuer Token und neue Generation
            if (isActive && (!_wasActive || current!.EinsatzDatum != _activeEinsatzDatum))
            {
                _currentMasterToken = GenerateRandomToken(MasterTokenLength);
                _currentGeneration = GenerationFor(current!);
                _activeEinsatzDatum = current!.EinsatzDatum;
                changed = true;
            }
            else if (!isActive && _wasActive)
            {
                _currentMasterToken = null;
                _currentGeneration = RandomGeneration();
                changed = true;
            }
            _wasActive = isActive;
        }

        if (changed)
            GenerationChanged?.Invoke();
    }

    // Generation je Einsatz statt fortlaufendem Zaehler: Ein Zaehler beginnt nach jedem Neustart wieder bei 0,
    // dann passten Cookies aus einem frueheren Einsatz wieder. So bleiben Cookies nur innerhalb desselben
    // Einsatzes gueltig – auch ueber einen Neustart des Servers hinweg. Grundlage ist der Startzeitpunkt
    // (EinsatzDatum), weil sich Einsatznummer und Ort waehrend des Einsatzes noch aendern koennen.
    private int GenerationFor(Models.EinsatzData einsatz)
    {
        using var hmac = new HMACSHA256(_hmacSecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"generation|{einsatz.EinsatzDatum.Ticks}"));
        return BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }

    private static int RandomGeneration() => RandomNumberGenerator.GetInt32(int.MaxValue);

    private static string GenerateRandomToken(int length)
    {
        var buffer = new char[length];
        Span<byte> randomBytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(randomBytes);
        for (int i = 0; i < length; i++)
            buffer[i] = TokenAlphabet[randomBytes[i] % TokenAlphabet.Length];
        return new string(buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _einsatzService.EinsatzChanged -= OnEinsatzChanged;
        _disposed = true;
    }
}
