using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class NotWhatCD : Gazelle.Gazelle
    {
        public override string Name => "notwhat.cd";
        public override string[] IndexerUrls => new string[] { "https://notwhat.cd/" };
        public override string Description => "NotWhat.CD (NWCD) is a private Music tracker that arised after the former (WCD) shut down.";
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public NotWhatCD(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }
    }
}
