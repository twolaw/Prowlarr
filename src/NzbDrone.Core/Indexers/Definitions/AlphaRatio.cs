using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class AlphaRatio : Gazelle.Gazelle
    {
        public override string Name => "AlphaRatio";
        public override string[] IndexerUrls => new string[] { "https://alpharatio.cc/" };
        public override string Description => "AlphaRatio(AR) is a Private Torrent Tracker for 0DAY / GENERAL";
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public AlphaRatio(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new AlphaRatioRequestGenerator()
            {
                Settings = Settings,
                HttpClient = _httpClient,
                Logger = _logger,
                Capabilities = Capabilities
            };
        }
    }

    public class AlphaRatioRequestGenerator : Gazelle.GazelleRequestGenerator
    {
        protected override bool ImdbInTags => true;
    }
}
