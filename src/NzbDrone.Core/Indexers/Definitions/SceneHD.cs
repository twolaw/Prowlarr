using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentValidation;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerVersions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions
{
    public class SceneHD : TorrentIndexerBase<SceneHDSettings>
    {
        public override string Name => "SceneHD";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public SceneHD(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new SceneHDRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new SceneHDParser(Settings, Capabilities.Categories);
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            return false;
        }
    }

    public class SceneHDRequestGenerator : IIndexerRequestGenerator
    {
        public SceneHDSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }
        public string BaseUrl { get; set; }

        public SceneHDRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories, string imdbId = null)
        {
            var search = new[] { imdbId, term };

            var qc = new NameValueCollection
            {
                { "api", "" },
                { "passkey", Settings.Passkey },
                { "search", string.Join(" ", search.Where(s => s.IsNotNullOrWhiteSpace())) }
            };

            foreach (var cat in Capabilities.Categories.MapTorznabCapsToTrackers(categories))
            {
                qc.Add("categories[" + cat + "]", "1");
            }

            var searchUrl = string.Format("{0}/browse.php?{1}", Settings.BaseUrl.TrimEnd('/'), qc.GetQueryString());

            var request = new IndexerRequest(searchUrl, HttpAccept.Json);

            yield return request;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories, searchCriteria.FullImdbId));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedTvSearchString), searchCriteria.Categories, searchCriteria.FullImdbId));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

            return pageableRequests;
        }

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class SceneHDParser : IParseIndexerResponse
    {
        private readonly SceneHDSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public SceneHDParser(SceneHDSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var detailsUrl = _settings.BaseUrl + "details.php?";
            var downloadUrl = _settings.BaseUrl + "download.php?";

            if (indexerResponse.Content?.Contains("User not found or passkey not set") == true)
            {
                throw new IndexerAuthException("The passkey is invalid. Check the indexer configuration.");
            }

            var jsonContent = JArray.Parse(indexerResponse.Content);

            foreach (var item in jsonContent)
            {
                var title = item.Value<string>("name");

                var id = item.Value<long>("id");
                var details = new Uri(detailsUrl + "id=" + id).AbsoluteUri;
                var link = new Uri(downloadUrl + "id=" + id + "&passkey=" + _settings.Passkey).AbsoluteUri;
                var publishDate = DateTime.ParseExact(item.Value<string>("added"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var dlVolumeFactor = item.Value<int>("is_freeleech") == 1 ? 0 : 1;

                var release = new TorrentInfo
                {
                    Title = title,
                    DownloadUrl = link,
                    InfoUrl = details,
                    Guid = details,
                    Categories = _categories.MapTrackerCatToNewznab(item.Value<string>("category")),
                    PublishDate = publishDate,
                    Size = item.Value<long>("size"),
                    Grabs = item.Value<int>("times_completed"),
                    Files = item.Value<int>("numfiles"),
                    Seeders = item.Value<int>("seeders"),
                    Peers = item.Value<int>("leechers") + item.Value<int>("seeders"),
                    ImdbId = ParseUtil.GetImdbID(item.Value<string>("imdbid")) ?? 0,
                    MinimumRatio = 1,
                    MinimumSeedTime = 0,
                    DownloadVolumeFactor = dlVolumeFactor,
                    UploadVolumeFactor = 1
                };

                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class SceneHDSettingsValidator : AbstractValidator<SceneHDSettings>
    {
        public SceneHDSettingsValidator()
        {
            RuleFor(c => c.Passkey).NotEmpty().Length(32);
        }
    }

    public class SceneHDSettings : IIndexerSettings
    {
        private static readonly SceneHDSettingsValidator Validator = new SceneHDSettingsValidator();

        public SceneHDSettings()
        {
            Passkey = "";
        }

        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "Passkey", Advanced = false, HelpText = "Site Passkey")]
        public string Passkey { get; set; }

        [FieldDefinition(3)]
        public IndexerBaseSettings BaseSettings { get; set; } = new IndexerBaseSettings();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
