// GPS-Halsband für Live-Tracking von Rettungshunden
// Wird von externer Halsband-Software über REST-API gespeist

namespace Einsatzueberwachung.Domain.Models
{
    public class Collar
    {
        public string Id { get; set; }
        public string CollarName { get; set; }
        public bool IsAssigned { get; set; }
        public string? AssignedTeamId { get; set; }

        public Collar()
        {
            Id = string.Empty;
            CollarName = string.Empty;
            IsAssigned = false;
            AssignedTeamId = null;
        }

        public Collar(string id, string collarName)
        {
            Id = id;
            CollarName = collarName;
            IsAssigned = false;
            AssignedTeamId = null;
        }
    }
}
