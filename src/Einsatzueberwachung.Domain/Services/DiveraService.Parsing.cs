using System.Text.Json;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DiveraService
    {
        private DiveraAlarm? ParseLastAlarmResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                    return null;

                if (!root.TryGetProperty("data", out var dataEl))
                    return null;

                if (dataEl.ValueKind == JsonValueKind.Null || dataEl.ValueKind == JsonValueKind.Undefined)
                    return null;

                var alarmEl = dataEl;
                if (dataEl.TryGetProperty("alarm", out var innerEl) && innerEl.ValueKind == JsonValueKind.Object)
                    alarmEl = innerEl;

                if (alarmEl.ValueKind != JsonValueKind.Object)
                    return null;

                if (!alarmEl.TryGetProperty("id", out var idEl) || idEl.GetInt32() == 0)
                    return null;

                bool closed = false;
                if (alarmEl.TryGetProperty("closed", out var closedEl))
                {
                    closed = closedEl.ValueKind == JsonValueKind.True ||
                             (closedEl.ValueKind == JsonValueKind.Number && closedEl.GetInt64() != 0);
                }

                if (closed) return null;

                var alarm = new DiveraAlarm
                {
                    Id = idEl.GetInt32(),
                    ForeignId = alarmEl.TryGetProperty("foreign_id", out var fidEl) ? fidEl.GetString() ?? string.Empty : string.Empty,
                    Title = alarmEl.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty,
                    Text = alarmEl.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    Address = alarmEl.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? string.Empty : string.Empty,
                    Lat = alarmEl.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null,
                    Lng = alarmEl.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null,
                    Date = alarmEl.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
                        ? ConvertUnixToAppTime(dateEl.GetInt64())
                        : DateTime.MinValue,
                    Closed = false,
                    Priority = alarmEl.TryGetProperty("priority", out var prioEl) && prioEl.ValueKind == JsonValueKind.True,
                    Caller = alarmEl.TryGetProperty("caller", out var callerEl) ? callerEl.GetString() ?? string.Empty : string.Empty,
                    Remark = alarmEl.TryGetProperty("remark", out var remarkEl) ? remarkEl.GetString() ?? string.Empty : string.Empty,
                };

                var addressedIds = new HashSet<int>();
                if (alarmEl.TryGetProperty("ucr_addressed", out var addressedEl) && addressedEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uid in addressedEl.EnumerateArray())
                    {
                        if (uid.ValueKind == JsonValueKind.Number)
                        {
                            var id = uid.GetInt32();
                            addressedIds.Add(id);
                            alarm.AddressedUserIds.Add(id);
                        }
                    }
                }

                var answeredIds = new HashSet<int>();
                if (alarmEl.TryGetProperty("ucr_answered", out var answeredEl) && answeredEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var statusProp in answeredEl.EnumerateObject())
                    {
                        if (!int.TryParse(statusProp.Name, out var statusId)) continue;
                        if (statusProp.Value.ValueKind != JsonValueKind.Object) continue;

                        foreach (var userProp in statusProp.Value.EnumerateObject())
                        {
                            if (!int.TryParse(userProp.Name, out var userId)) continue;
                            if (answeredIds.Contains(userId)) continue;

                            answeredIds.Add(userId);
                            alarm.Ucr[userId] = statusId;
                            alarm.UcrDetails.Add(new DiveraUcrEntry
                            {
                                MemberId = userId,
                                MemberName = $"#{userId}",
                                Status = statusId
                            });
                        }
                    }
                }

                foreach (var addressedId in addressedIds)
                {
                    if (!answeredIds.Contains(addressedId))
                    {
                        alarm.Ucr[addressedId] = 0;
                        alarm.UcrDetails.Add(new DiveraUcrEntry
                        {
                            MemberId = addressedId,
                            MemberName = $"#{addressedId}",
                            Status = 0
                        });
                    }
                }

                _logger.LogInformation("Divera LastAlarm geparst: ID={Id}, Titel='{Title}', Adressiert={Addressed}, Geantwortet={Answered}",
                    alarm.Id, alarm.Title, addressedIds.Count, answeredIds.Count);

                return alarm;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Parsen der Divera LastAlarm-Antwort");
                return null;
            }
        }

        private DiveraPullResponse? ParsePullAllResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                {
                    _logger.LogWarning("Divera API: success=false in der Antwort");
                    return null;
                }

                if (!root.TryGetProperty("data", out var dataElement))
                {
                    _logger.LogWarning("Divera API: Kein 'data'-Feld in der Antwort");
                    return null;
                }

                _logger.LogDebug("Divera PullAll: Data-Schluessel: {Keys}",
                    string.Join(", ", dataElement.EnumerateObject().Select(p => p.Name)));

                var result = new DiveraPullResponse
                {
                    LastUpdated = Ortszeit.Jetzt
                };

                if (dataElement.TryGetProperty("cluster", out var clusterEl) &&
                    clusterEl.TryGetProperty("status_sorter", out var statusSorterEl) &&
                    statusSorterEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in statusSorterEl.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var statusId))
                        {
                            var obj = prop.Value;
                            var def = new DiveraStatusDefinition
                            {
                                Id = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : statusId,
                                Name = obj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                                Color = obj.TryGetProperty("color", out var colorEl) ? colorEl.GetString() ?? string.Empty : string.Empty
                            };
                            result.StatusDefinitions[def.Id] = def;
                        }
                    }
                }

                var memberStatusMap = new Dictionary<int, int>();
                if (dataElement.TryGetProperty("cluster", out var clusterForStatus) &&
                    clusterForStatus.TryGetProperty("status", out var clusterStatusEl) &&
                    clusterStatusEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in clusterStatusEl.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var memberId) && prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            memberStatusMap[memberId] = prop.Value.GetInt32();
                        }
                    }
                }

                if (dataElement.TryGetProperty("cluster_members", out var membersEl) &&
                    membersEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in membersEl.EnumerateObject())
                    {
                        var obj = prop.Value;
                        var memberId = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                        if (memberId == 0 && int.TryParse(prop.Name, out var parsedId))
                            memberId = parsedId;

                        var member = new DiveraMember
                        {
                            Id = memberId,
                            Firstname = obj.TryGetProperty("firstname", out var fnEl) ? fnEl.GetString() ?? string.Empty : string.Empty,
                            Lastname = obj.TryGetProperty("lastname", out var lnEl) ? lnEl.GetString() ?? string.Empty : string.Empty,
                        };

                        if (memberStatusMap.TryGetValue(memberId, out var statusId))
                        {
                            member.StatusId = statusId;
                            if (result.StatusDefinitions.TryGetValue(statusId, out var statusDef))
                            {
                                member.StatusName = statusDef.Name;
                                member.StatusColor = statusDef.Color;
                            }
                        }

                        if (obj.TryGetProperty("qualifications", out var qualsEl) && qualsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var q in qualsEl.EnumerateArray())
                            {
                                if (q.ValueKind == JsonValueKind.Number)
                                    member.QualificationIds.Add(q.GetInt32());
                            }
                        }

                        result.Members.Add(member);
                    }
                }

                if (dataElement.TryGetProperty("alarm", out var alarmElement) &&
                    alarmElement.TryGetProperty("items", out var itemsElement) &&
                    itemsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var alarmProp in itemsElement.EnumerateObject())
                    {
                        var obj = alarmProp.Value;
                        var alarm = new DiveraAlarm
                        {
                            Id = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                            ForeignId = obj.TryGetProperty("foreign_id", out var fidEl2) ? fidEl2.GetString() ?? string.Empty : string.Empty,
                            Title = obj.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty,
                            Text = obj.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                            Address = obj.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? string.Empty : string.Empty,
                            Lat = obj.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null,
                            Lng = obj.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null,
                            Date = obj.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
                                ? ConvertUnixToAppTime(dateEl.GetInt64())
                                : DateTime.MinValue,
                            Closed = obj.TryGetProperty("closed", out var closedEl) &&
                                     (closedEl.ValueKind == JsonValueKind.True ||
                                      (closedEl.ValueKind == JsonValueKind.Number && closedEl.GetInt64() != 0)),
                            Priority = obj.TryGetProperty("priority", out var prioEl) && prioEl.ValueKind == JsonValueKind.True,
                            Caller = obj.TryGetProperty("caller", out var callerEl2) ? callerEl2.GetString() ?? string.Empty : string.Empty,
                            Remark = obj.TryGetProperty("remark", out var remarkEl2) ? remarkEl2.GetString() ?? string.Empty : string.Empty,
                        };

                        if (obj.TryGetProperty("ucr", out var ucrEl) && ucrEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var ucrProp in ucrEl.EnumerateObject())
                            {
                                if (int.TryParse(ucrProp.Name, out var ucrMemberId) && ucrProp.Value.ValueKind == JsonValueKind.Number)
                                {
                                    alarm.Ucr[ucrMemberId] = ucrProp.Value.GetInt32();
                                }
                            }
                        }

                        var memberLookup = result.Members.ToDictionary(m => m.Id, m => m.FullName);
                        foreach (var (ucrMemberId, ucrStatus) in alarm.Ucr)
                        {
                            alarm.UcrDetails.Add(new DiveraUcrEntry
                            {
                                MemberId = ucrMemberId,
                                MemberName = memberLookup.TryGetValue(ucrMemberId, out var name) ? name : $"#{ucrMemberId}",
                                Status = ucrStatus
                            });
                        }

                        result.Alarms.Add(alarm);
                    }
                }

                _logger.LogInformation("Divera PullAll geparst: {AlarmCount} Alarme, {MemberCount} Mitglieder",
                    result.Alarms.Count, result.Members.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Parsen der Divera PullAll-Antwort");
                return null;
            }
        }
    }
}
