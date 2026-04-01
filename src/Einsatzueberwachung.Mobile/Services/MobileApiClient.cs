using System.Net.Http.Json;
using System.Text.Json;
using Einsatzueberwachung.Domain.Models;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Mobile.Services;

public sealed class MobileApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MobileApiClient> _logger;

    public MobileApiClient(HttpClient httpClient, IConfiguration configuration, NavigationManager navigationManager, ILogger<MobileApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var configuredBaseUrl = configuration["ServerApiBaseUrl"];
        var runtimeBaseUrl = new Uri(navigationManager.BaseUri).GetLeftPart(UriPartial.Authority);
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? runtimeBaseUrl
            : configuredBaseUrl.TrimEnd('/');

        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<bool> StartEinsatzAsync(EinsatzData data, string? initialNote)
    {
        var request = new StartEinsatzRequest(
            data.Einsatzort,
            data.Einsatzleiter,
            data.Fuehrungsassistent,
            data.Alarmiert,
            data.EinsatzDatum,
            data.EinsatzNummer,
            data.IstEinsatz,
            initialNote);

        return await PostAndCheckAsync("/api/einsatz/start", request);
    }

    public async Task<IReadOnlyList<Team>> GetTeamsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<TeamsEnvelope>("/api/einsatz/teams");
            if (response?.Teams is null)
            {
                return Array.Empty<Team>();
            }

            return response.Teams.Select(MapTeam).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Teams");
            return Array.Empty<Team>();
        }
    }

    public async Task<IReadOnlyList<GlobalNotesEntry>> GetNotesAsync(string filter)
    {
        try
        {
            var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "alle" : filter;
            var response = await _httpClient.GetFromJsonAsync<NotesEnvelope>($"/api/threads?filter={Uri.EscapeDataString(normalizedFilter)}");
            if (response?.Notes is null)
            {
                return Array.Empty<GlobalNotesEntry>();
            }

            return response.Notes.Select(MapNote).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Notizen");
            return Array.Empty<GlobalNotesEntry>();
        }
    }

    public async Task<bool> AddNoteAsync(string text, string sourceType, string sourceTeamId, string sourceTeamName, string createdBy)
    {
        var payload = new
        {
            text,
            sourceType,
            sourceTeamId,
            sourceTeamName,
            createdBy
        };

        return await PostAndCheckAsync("/api/threads", payload);
    }

    public async Task<bool> AddReplyAsync(string noteId, string text, string sourceTeamId, string sourceTeamName, string createdBy)
    {
        var payload = new
        {
            text,
            sourceTeamId,
            sourceTeamName,
            createdBy
        };

        return await PostAndCheckAsync($"/api/threads/{Uri.EscapeDataString(noteId)}/replies", payload);
    }

    public async Task<IReadOnlyList<GlobalNotesEntry>> GetRadioMessagesAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RadioEnvelope>("/api/radio");
            if (response?.Messages is null)
            {
                return Array.Empty<GlobalNotesEntry>();
            }

            return response.Messages.Select(MapRadio).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Funksprueche");
            return Array.Empty<GlobalNotesEntry>();
        }
    }

    public async Task<bool> AddRadioMessageAsync(string text, string sourceTeamId, string sourceTeamName, string createdBy)
    {
        var payload = new
        {
            text,
            sourceTeamId,
            sourceTeamName,
            createdBy
        };

        return await PostAndCheckAsync("/api/radio", payload);
    }

    public async Task<bool> AddRadioReplyAsync(string messageId, string text, string sourceTeamId, string sourceTeamName, string createdBy)
    {
        var payload = new
        {
            text,
            sourceTeamId,
            sourceTeamName,
            createdBy
        };

        return await PostAndCheckAsync($"/api/radio/{Uri.EscapeDataString(messageId)}/replies", payload);
    }

    private async Task<bool> PostAndCheckAsync<TPayload>(string path, TPayload payload)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(path, payload);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("API call {Path} failed with {Status}: {Body}", path, response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API call {Path} failed", path);
            return false;
        }
    }

    private static Team MapTeam(TeamDto dto)
    {
        return new Team
        {
            TeamId = dto.TeamId,
            TeamName = dto.TeamName,
            DogName = dto.DogName,
            HundefuehrerName = dto.HundefuehrerName,
            HelferName = dto.HelferName,
            SearchAreaName = dto.SearchAreaName,
            ElapsedTime = dto.ElapsedTime,
            IsRunning = dto.IsRunning,
            IsFirstWarning = dto.IsFirstWarning,
            IsSecondWarning = dto.IsSecondWarning,
            FirstWarningMinutes = dto.FirstWarningMinutes,
            SecondWarningMinutes = dto.SecondWarningMinutes,
            IsDroneTeam = dto.IsDroneTeam,
            IsSupportTeam = dto.IsSupportTeam
        };
    }

    private static GlobalNotesEntry MapNote(NoteDto dto)
    {
        return new GlobalNotesEntry
        {
            Id = dto.Id,
            Timestamp = dto.Timestamp,
            Text = dto.Text,
            SourceTeamId = dto.SourceTeamId,
            SourceTeamName = dto.SourceTeamName,
            SourceType = dto.SourceType,
            CreatedBy = dto.CreatedBy,
            Replies = dto.Replies?.Select(MapReply).ToList() ?? new List<GlobalNotesReply>()
        };
    }

    private static GlobalNotesEntry MapRadio(RadioMessageDto dto)
    {
        return new GlobalNotesEntry
        {
            Id = dto.Id,
            Timestamp = dto.Timestamp,
            Text = dto.Text,
            SourceTeamId = dto.SourceTeamId,
            SourceTeamName = dto.SourceTeamName,
            SourceType = "Funk",
            CreatedBy = dto.CreatedBy,
            Replies = dto.Replies?.Select(MapRadioReply).ToList() ?? new List<GlobalNotesReply>()
        };
    }

    private static GlobalNotesReply MapRadioReply(RadioReplyDto dto)
    {
        return new GlobalNotesReply
        {
            Id = dto.Id,
            NoteId = dto.MessageId,
            Timestamp = dto.Timestamp,
            Text = dto.Text,
            SourceTeamId = dto.SourceTeamId,
            SourceTeamName = dto.SourceTeamName,
            CreatedBy = dto.CreatedBy
        };
    }

    private static GlobalNotesReply MapReply(ReplyDto dto)
    {
        return new GlobalNotesReply
        {
            Id = dto.Id,
            NoteId = dto.NoteId,
            Timestamp = dto.Timestamp,
            Text = dto.Text,
            SourceTeamId = dto.SourceTeamId,
            SourceTeamName = dto.SourceTeamName,
            CreatedBy = dto.CreatedBy
        };
    }

    private sealed record StartEinsatzRequest(
        string Einsatzort,
        string Einsatzleiter,
        string? Fuehrungsassistent,
        string? Alarmiert,
        DateTime? EinsatzDatum,
        string? EinsatzNummer,
        bool IstEinsatz,
        string? Bemerkung);

    private sealed record TeamsEnvelope(List<TeamDto> Teams);
    private sealed record NotesEnvelope(List<NoteDto> Notes);
    private sealed record RadioEnvelope(List<RadioMessageDto> Messages);

    private sealed record TeamDto(
        string TeamId,
        string TeamName,
        string DogName,
        string HundefuehrerName,
        string HelferName,
        string SearchAreaName,
        TimeSpan ElapsedTime,
        bool IsRunning,
        bool IsFirstWarning,
        bool IsSecondWarning,
        int FirstWarningMinutes,
        int SecondWarningMinutes,
        bool IsDroneTeam,
        bool IsSupportTeam,
        string Status);

    private sealed record NoteDto(
        string Id,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string SourceType,
        string CreatedBy,
        bool IsEdited,
        List<ReplyDto> Replies,
        string FormattedTimestamp,
        string FormattedDate);

    private sealed record ReplyDto(
        string Id,
        string NoteId,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string CreatedBy,
        string FormattedTimestamp);

    private sealed record RadioMessageDto(
        string Id,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string CreatedBy,
        List<RadioReplyDto> Replies,
        string FormattedTimestamp);

    private sealed record RadioReplyDto(
        string Id,
        string MessageId,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string CreatedBy,
        string FormattedTimestamp);
}
