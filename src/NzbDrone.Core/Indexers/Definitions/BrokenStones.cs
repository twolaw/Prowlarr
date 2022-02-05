using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class BrokenStones : Gazelle.Gazelle
    {
        public override string Name => "BrokenStones";
        public override string[] IndexerUrls => new string[] { "https://brokenstones.club/" };
        public override string Description => "Broken Stones is a Private site for MacOS and iOS APPS / GAMES";
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public BrokenStones(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }
    }
}
