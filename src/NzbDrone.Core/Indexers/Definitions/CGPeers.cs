using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class CGPeers : Gazelle.Gazelle
    {
        public override string Name => "CGPeers";
        public override string[] IndexerUrls => new string[] { "https://cgpeers.to/" };
        public override string Description => "CGPeers is a Private Torrent Tracker for GRAPHICS SOFTWARE / TUTORIALS / ETC";
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public CGPeers(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }
    }
}
