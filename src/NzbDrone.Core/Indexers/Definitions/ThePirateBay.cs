using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions
{
    [Obsolete("Moved to YML for Cardigann v3")]
    public class ThePirateBay : TorrentIndexerBase<ThePirateBaySettings>
    {
        public override string Name => "ThePirateBay";
        public override string[] IndexerUrls => new string[] { "https://thepiratebay.org/" };
        public override string Description => "Pirate Bay(TPB) is the galaxy’s most resilient Public BitTorrent site";
        public override string Language => "en-US";
        public override Encoding Encoding => Encoding.UTF8;
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Public;

        public ThePirateBay(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new ThePirateBayRequestGenerator() { Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new ThePirateBayParser(Capabilities.Categories, Settings);
        }
    }

    public class ThePirateBayRequestGenerator : IIndexerRequestGenerator
    {
        public IndexerCapabilities Capabilities { get; set; }

        private static string ApiUrl => "https://apibay.org/";

        public ThePirateBayRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories, bool rssSearch)
        {
            if (rssSearch)
            {
                yield return new IndexerRequest($"{ApiUrl.TrimEnd('/')}/precompiled/data_top100_recent.json", HttpAccept.Html);
            }
            else
            {
                var cats = Capabilities.Categories.MapTorznabCapsToTrackers(categories);

                var queryStringCategories = string.Join(
                    ",",
                    cats.Count == 0
                        ? Capabilities.Categories.GetTrackerCategories()
                        : cats);

                var queryCollection = new NameValueCollection
                {
                    { "q", term },
                    { "cat", queryStringCategories }
                };

                var searchUrl = string.Format("{0}/q.php?{1}", ApiUrl.TrimEnd('/'), queryCollection.GetQueryString());

                var request = new IndexerRequest(searchUrl, HttpAccept.Json);

                yield return request;
            }
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.RssSearch));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.RssSearch));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedTvSearchString), searchCriteria.Categories, searchCriteria.RssSearch));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.RssSearch));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.RssSearch));

            return pageableRequests;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class ThePirateBayParser : IParseIndexerResponse
    {
        private readonly ThePirateBaySettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public ThePirateBayParser(IndexerCapabilitiesCategories categories, ThePirateBaySettings settings)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var queryResponseItems = JsonConvert.DeserializeObject<List<ThePirateBayTorrent>>(indexerResponse.Content);

            // The API returns a single item to represent a state of no results. Avoid returning this result.
            if (queryResponseItems.Count == 1 && queryResponseItems.First().Id == 0)
            {
                return torrentInfos;
            }

            foreach (var item in queryResponseItems)
            {
                var details = item.Id == 0 ? null : $"{_settings.BaseUrl}description.php?id={item.Id}";
                var imdbId = string.IsNullOrEmpty(item.Imdb) ? null : ParseUtil.GetImdbID(item.Imdb);
                var torrentItem =  new TorrentInfo
                {
                    Title = item.Name,
                    Categories = _categories.MapTrackerCatToNewznab(item.Category.ToString()),
                    Guid = details,
                    InfoUrl = details,
                    InfoHash = item.InfoHash, // magnet link is auto generated from infohash
                    PublishDate = DateTimeUtil.UnixTimestampToDateTime(item.Added),
                    Seeders = item.Seeders,
                    Peers = item.Seeders + item.Leechers,
                    Size = item.Size,
                    Files = item.NumFiles,
                    DownloadVolumeFactor = 0,
                    UploadVolumeFactor = 1,
                    ImdbId = imdbId.GetValueOrDefault()
                };

                if (item.InfoHash != null)
                {
                    torrentItem.MagnetUrl = MagnetLinkBuilder.BuildPublicMagnetLink(item.InfoHash, item.Name);
                }

                torrentInfos.Add(torrentItem);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class ThePirateBaySettings : IIndexerSettings
    {
        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2)]
        public IndexerBaseSettings BaseSettings { get; set; } = new IndexerBaseSettings();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }

    public class ThePirateBayTorrent
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("info_hash")]
        public string InfoHash { get; set; }

        [JsonProperty("leechers")]
        public int Leechers { get; set; }

        [JsonProperty("seeders")]
        public int Seeders { get; set; }

        [JsonProperty("num_files")]
        public int NumFiles { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("added")]
        public long Added { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("category")]
        public long Category { get; set; }

        [JsonProperty("imdb")]
        public string Imdb { get; set; }
    }
}
