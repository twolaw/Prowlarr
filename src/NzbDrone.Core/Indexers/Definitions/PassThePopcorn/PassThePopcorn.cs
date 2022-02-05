using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.PassThePopcorn
{
    public class PassThePopcorn : TorrentIndexerBase<PassThePopcornSettings>
    {
        public override string Name => "PassThePopcorn";
        public override string[] IndexerUrls => new string[] { "https://passthepopcorn.me" };
        public override string Description => "PassThePopcorn (PTP) is a Private site for MOVIES / TV";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;
        public override bool SupportsRss => true;
        public override bool SupportsSearch => true;

        public override int PageSize => 50;

        public PassThePopcorn(IIndexerHttpClient httpClient,
                              IEventAggregator eventAggregator,
                              ICacheManager cacheManager,
                              IIndexerStatusService indexerStatusService,
                              IIndexerDefinitionUpdateService definitionService,
                              IConfigService configService,
                              Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new PassThePopcornRequestGenerator()
            {
                Settings = Settings,
                HttpClient = _httpClient,
                Logger = _logger
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new PassThePopcornParser(Settings, Capabilities, _logger);
        }
    }

    public class PassThePopcornFlag : IndexerFlag
    {
        public static IndexerFlag Golden => new IndexerFlag("golden", "Release follows Golden Popcorn quality rules");
        public static IndexerFlag Approved => new IndexerFlag("approved", "Release approved by PTP");
    }
}
