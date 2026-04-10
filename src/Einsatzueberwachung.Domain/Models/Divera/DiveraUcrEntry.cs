namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraUcrEntry
    {
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public int Status { get; set; }

        public string StatusText => Status switch
        {
            1 => "Kommt",
            2 => "Kommt nicht",
            3 => "Kommt später",
            _ => "Keine Rückmeldung"
        };

        public string StatusBadgeCss => Status switch
        {
            1 => "bg-success",
            2 => "bg-danger",
            3 => "bg-warning text-dark",
            _ => "bg-secondary"
        };
    }
}
