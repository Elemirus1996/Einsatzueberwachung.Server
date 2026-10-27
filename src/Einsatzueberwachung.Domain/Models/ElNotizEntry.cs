using System;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class ElNotizEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = Ortszeit.Jetzt;
        public string Text { get; set; } = string.Empty;
        public string Prefix { get; set; } = string.Empty;

        public string FormattedTimestamp => Timestamp.ToString("HH:mm");
        public string FormattedDateTime => Timestamp.ToString("dd.MM. HH:mm");

        public string PrefixIcon => Prefix switch
        {
            "Anruf" => "bi-telephone-fill",
            "Funk"  => "bi-broadcast",
            "Wichtig" => "bi-exclamation-triangle-fill",
            _ => "bi-pencil"
        };

        public string PrefixColor => Prefix switch
        {
            "Anruf" => "primary",
            "Funk"  => "info",
            "Wichtig" => "danger",
            _ => "secondary"
        };
    }
}
