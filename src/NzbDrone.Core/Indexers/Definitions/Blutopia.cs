using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Definitions.UNIT3D;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Definitions
{
    [Obsolete("Moved to YML for Cardigann v3")]
    public class Blutopia : Unit3dBase
    {
        public override string Name => "Blutopia";
        public override string[] IndexerUrls => new string[] { "https://blutopia.xyz/" };
        public override string Description => "Blutopia (BLU) is a Private Torrent Tracker for HD MOVIES / TV";
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public Blutopia(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        protected override IndexerCapabilities SetCapabilities()
        {
            var caps = new IndexerCapabilities
            {
                TvSearchParams = new List<TvSearchParam>
                       {
                           TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep, TvSearchParam.ImdbId, TvSearchParam.TvdbId
                       },
                MovieSearchParams = new List<MovieSearchParam>
                       {
                           MovieSearchParam.Q, MovieSearchParam.ImdbId, MovieSearchParam.TmdbId
                       },
                MusicSearchParams = new List<MusicSearchParam>
                       {
                           MusicSearchParam.Q
                       },
                BookSearchParams = new List<BookSearchParam>
                       {
                           BookSearchParam.Q
                       }
            };

            caps.Categories.AddCategoryMapping(1, NewznabStandardCategory.Movies, "Movie");
            caps.Categories.AddCategoryMapping(2, NewznabStandardCategory.TV, "TV Show");
            caps.Categories.AddCategoryMapping(3, NewznabStandardCategory.MoviesOther, "FANRES");
            caps.Categories.AddCategoryMapping(5, NewznabStandardCategory.MoviesOther, "Trailer");

            return caps;
        }
    }
}
