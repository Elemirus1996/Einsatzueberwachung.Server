using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzMergeService : IEinsatzMergeService
    {
        private readonly IEinsatzService _einsatzService;
        private readonly IMasterDataService _masterDataService;
        private readonly IArchivService _archivService;
        private readonly ITimeService? _timeService;

        private const string SearchAreaRenameSuffix = "_importiert";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private DateTime Now => _timeService?.Now ?? Ortszeit.Jetzt;

        public EinsatzMergeService(
            IEinsatzService einsatzService,
            IMasterDataService masterDataService,
            IArchivService archivService,
            ITimeService? timeService = null)
        {
            _einsatzService = einsatzService;
            _masterDataService = masterDataService;
            _archivService = archivService;
            _timeService = timeService;
        }
    }
}
