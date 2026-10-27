using System.Globalization;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService : IEinsatzService
    {
        private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");
        private static readonly string[] AlarmDateFormats =
        {
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss"
        };

        private static readonly string[] AlarmTimeFormats =
        {
            "H:mm",
            "HH:mm",
            "H:mm:ss",
            "HH:mm:ss"
        };

        private readonly ISettingsService? _settingsService;
        private readonly ITimeService? _timeService;
        private readonly IMasterDataService? _masterDataService;
        private EinsatzData _currentEinsatz;
        private readonly List<Team> _teams;
        private readonly List<GlobalNotesEntry> _globalNotes;
        private readonly List<GlobalNotesHistory> _noteHistory;
        private readonly Dictionary<string, DogPauseRecord> _dogPauses = new();
        private readonly Dictionary<string, TeamPhoneLocation> _phoneLocations = new();

        public EinsatzData CurrentEinsatz => _currentEinsatz;
        public List<Team> Teams => _teams;
        public List<GlobalNotesEntry> GlobalNotes => _globalNotes;
        public IReadOnlyDictionary<string, TeamPhoneLocation> PhoneLocations => _phoneLocations;

        public event Action? EinsatzChanged;
        public event Action<Team>? TeamAdded;
        public event Action<Team>? TeamRemoved;
        public event Action<Team>? TeamUpdated;
        public event Action<GlobalNotesEntry>? NoteAdded;
        public event Action<Team, bool>? TeamWarningTriggered;
        public event Action? VermisstenInfoChanged;
        public event Action<VermisstenInfo>? VermisstenAdded;
        public event Action<VermisstenInfo>? VermisstenUpdated;
        public event Action<Guid>? VermisstenRemoved;
        public event Action? SzenarioChanged;
        public event Action<TruemmerKarte>? TruemmerKarteAdded;
        public event Action<Guid>? TruemmerKarteRemoved;
        public event Action<TruemmerArea>? TruemmerAreaUpserted;
        public event Action<Guid>? TruemmerAreaRemoved;
        public event Action? ElNotizAdded;
        public event Action<string, string, TeamPhoneLocation>? TeamPhoneLocationChanged;
        public event Action<string, string, TeamPhoneLocation>? TeamPhoneTrackPointAdded;
        public event Action<Team>? DogPauseStarted;

        public EinsatzService(ISettingsService? settingsService = null, ITimeService? timeService = null, IMasterDataService? masterDataService = null)
        {
            _settingsService = settingsService;
            _timeService = timeService;
            _masterDataService = masterDataService;
            _currentEinsatz = new EinsatzData();
            _teams = new List<Team>();
            _globalNotes = new List<GlobalNotesEntry>();
            _noteHistory = new List<GlobalNotesHistory>();
            EnsureCurrentEinsatzTeamReference();
        }

        private DateTime GetServerNowLocal() => _timeService?.Now ?? DateTimeOffset.Now.LocalDateTime;

        private void EnsureAlarmTime(EinsatzData einsatzData)
        {
            if (!einsatzData.AlarmierungsZeit.HasValue && TryParseAlarmText(einsatzData.Alarmiert, out var parsedAlarm))
                einsatzData.AlarmierungsZeit = parsedAlarm;

            if (!einsatzData.AlarmierungsZeit.HasValue)
                einsatzData.AlarmierungsZeit = GetServerNowLocal();

            einsatzData.Alarmiert = einsatzData.AlarmierungsZeit.Value.ToString("dd.MM.yyyy HH:mm", DeCulture);
        }

        private bool TryParseAlarmText(string? alarmText, out DateTime parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(alarmText))
                return false;

            var input = alarmText.Trim();

            if (DateTime.TryParseExact(input, AlarmDateFormats, DeCulture, DateTimeStyles.AssumeLocal, out parsed))
                return true;

            if (DateTime.TryParseExact(input, AlarmTimeFormats, DeCulture, DateTimeStyles.None, out var parsedTimeOnly))
            {
                var now = GetServerNowLocal();
                parsed = new DateTime(now.Year, now.Month, now.Day, parsedTimeOnly.Hour, parsedTimeOnly.Minute, parsedTimeOnly.Second, DateTimeKind.Unspecified);
                if (parsed > now.AddMinutes(1))
                    parsed = parsed.AddDays(-1);
                return true;
            }

            return DateTime.TryParse(input, DeCulture, DateTimeStyles.AssumeLocal, out parsed)
                || DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed);
        }

        private async Task ApplyStaffelFallbackAsync(EinsatzData einsatzData)
        {
            if (_settingsService is null)
                return;

            var settings = await _settingsService.GetStaffelSettingsAsync();

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelName)) einsatzData.StaffelName = settings.StaffelName;
            if (string.IsNullOrWhiteSpace(einsatzData.StaffelAdresse)) einsatzData.StaffelAdresse = settings.StaffelAdresse;
            if (string.IsNullOrWhiteSpace(einsatzData.StaffelTelefon)) einsatzData.StaffelTelefon = settings.StaffelTelefon;
            if (string.IsNullOrWhiteSpace(einsatzData.StaffelEmail)) einsatzData.StaffelEmail = settings.StaffelEmail;
            if (string.IsNullOrWhiteSpace(einsatzData.StaffelLogoPfad)) einsatzData.StaffelLogoPfad = settings.StaffelLogoPfad;
        }

        private void EnsureCurrentEinsatzTeamReference()
        {
            _currentEinsatz.Teams = _teams;
        }

        private static void CopyMutableTeamFields(Team target, Team source)
        {
            target.TeamName = source.TeamName;
            target.DogName = source.DogName;
            target.DogId = source.DogId;
            target.DogSpecialization = source.DogSpecialization;
            target.HundefuehrerName = source.HundefuehrerName;
            target.HundefuehrerId = source.HundefuehrerId;
            target.HelferIds = new List<string>(source.HelferIds);
            target.HelferNames = new List<string>(source.HelferNames);
            target.SearchAreaName = source.SearchAreaName;
            target.SearchAreaId = source.SearchAreaId;
            target.FirstWarningMinutes = source.FirstWarningMinutes;
            target.SecondWarningMinutes = source.SecondWarningMinutes;
            target.Notes = source.Notes;
            target.IsDroneTeam = source.IsDroneTeam;
            target.DroneType = source.DroneType;
            target.DroneId = source.DroneId;
            target.IsSupportTeam = source.IsSupportTeam;
            target.CollarId = source.CollarId;
            target.CollarName = source.CollarName;
            target.IsPausing = source.IsPausing;
            target.PauseStartTime = source.PauseStartTime;
            target.RunTimeBeforePause = source.RunTimeBeforePause;
            target.RequiredPauseMinutes = source.RequiredPauseMinutes;
        }

        private bool IsEinsatzAktiv()
            => !string.IsNullOrWhiteSpace(_currentEinsatz.Einsatzort) && _currentEinsatz.EinsatzEnde is null;
    }
}
