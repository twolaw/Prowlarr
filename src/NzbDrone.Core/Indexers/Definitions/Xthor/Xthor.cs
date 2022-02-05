using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions.Xthor
{
    [Obsolete("Moved to YML for Cardigann v5")]
    public class Xthor : TorrentIndexerBase<XthorSettings>
    {
        public override string Name => "Xthor";
        public override string[] IndexerUrls => new string[] { "https://api.xthor.tk/" };
        public override string Language => "fr-FR";
        public override string Description => "Xthor is a general Private torrent site";
        public override Encoding Encoding => Encoding.GetEncoding("windows-1252");
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public override TimeSpan RateLimit => TimeSpan.FromSeconds(2.5);

        public Xthor(IIndexerHttpClient httpClient,
                     IEventAggregator eventAggregator,
                     IIndexerStatusService indexerStatusService,
                     IIndexerDefinitionUpdateService definitionService,
                     IConfigService configService,
                     Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new XthorRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new XthorParser(Settings, Capabilities.Categories);
        }
    }
}
