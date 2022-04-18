using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using FluentValidation;
using Newtonsoft.Json.Linq;
using NLog;
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
    [Obsolete("Moved to YML for Cardigann v3")]
    public class TorrentSeeds : TorrentIndexerBase<TorrentSeedsSettings>
    {
        public override string Name => "TorrentSeeds";
        private string LoginUrl => Settings.BaseUrl + "takelogin.php";
        private string CaptchaUrl => Settings.BaseUrl + "simpleCaptcha.php?numImages=1";
        private string TokenUrl => Settings.BaseUrl + "login.php";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public TorrentSeeds(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new TorrentSeedsRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TorrentSeedsParser(Settings, Capabilities.Categories);
        }

        protected override async Task DoLogin()
        {
            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true
            };

            Cookies = null;

            var loginPage = await ExecuteAuth(new HttpRequest(CaptchaUrl));
            var json1 = JObject.Parse(loginPage.Content);
            var captchaSelection = json1["images"][0]["hash"];

            requestBuilder.Method = HttpMethod.Post;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);
            requestBuilder.SetCookies(loginPage.GetCookies());

            var authLoginRequest = requestBuilder
                .AddFormParameter("username", Settings.Username)
                .AddFormParameter("password", Settings.Password)
                .AddFormParameter("submitme", "X")
                .AddFormParameter("captchaSelection", (string)captchaSelection)
                .SetHeader("Content-Type", "multipart/form-data")
                .Build();

            var response = await ExecuteAuth(authLoginRequest);

            if (CheckIfLoginNeeded(response))
            {
                throw new IndexerAuthException("TorrentSeeds Login Failed");
            }

            var cookies = response.GetCookies();
            UpdateCookies(cookies, DateTime.Now + TimeSpan.FromDays(30));

            _logger.Debug("TorrentSeeds authentication succeeded.");
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            if ((httpResponse.HasHttpRedirect && httpResponse.Headers.GetSingleValue("Location").Contains("/login.php?")) ||
                (!httpResponse.HasHttpRedirect && !httpResponse.Content.Contains("/logout.php?")))
            {
                return true;
            }

            return false;
        }
    }

    public class TorrentSeedsRequestGenerator : IIndexerRequestGenerator
    {
        public TorrentSeedsSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        public TorrentSeedsRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories)
        {
            // remove operator characters
            var cleanSearchString = Regex.Replace(term.Trim(), "[ _.+-]+", " ", RegexOptions.Compiled);

            var searchUrl = Settings.BaseUrl + "browse_elastic.php";
            var queryCollection = new NameValueCollection
            {
                { "search_in", "name" },
                { "search_mode", "all" },
                { "order_by", "added" },
                { "order_way", "desc" }
            };

            if (!string.IsNullOrWhiteSpace(cleanSearchString))
            {
                queryCollection.Add("query", cleanSearchString);
            }

            foreach (var cat in Capabilities.Categories.MapTorznabCapsToTrackers(categories))
            {
                queryCollection.Add($"cat[{cat}]", "1");
            }

            searchUrl += "?" + queryCollection.GetQueryString();

            var request = new IndexerRequest(searchUrl, HttpAccept.Html);

            yield return request;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

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

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedTvSearchString), searchCriteria.Categories));

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

    public class TorrentSeedsParser : IParseIndexerResponse
    {
        private readonly TorrentSeedsSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public TorrentSeedsParser(TorrentSeedsSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<TorrentInfo>();

            var parser = new HtmlParser();
            var dom = parser.ParseDocument(indexerResponse.Content);
            var rows = dom.QuerySelectorAll("table.table-bordered > tbody > tr[class*=\"torrent_row_\"]");
            foreach (var row in rows)
            {
                var release = new TorrentInfo();
                release.MinimumRatio = 1;
                release.MinimumSeedTime = 72 * 60 * 60;
                var qCatLink = row.QuerySelector("a[href^=\"/browse_elastic.php?cat=\"]");
                var catStr = qCatLink.GetAttribute("href").Split('=')[1];
                release.Categories = _categories.MapTrackerCatToNewznab(catStr);
                var qDetailsLink = row.QuerySelector("a[href^=\"/details.php?id=\"]");
                var qDetailsTitle = row.QuerySelector("td:has(a[href^=\"/details.php?id=\"]) b");
                release.Title = qDetailsTitle.TextContent.Trim();
                var qDlLink = row.QuerySelector("a[href^=\"/download.php?torrent=\"]");

                release.DownloadUrl = _settings.BaseUrl + qDlLink.GetAttribute("href").TrimStart('/');
                release.InfoUrl = _settings.BaseUrl + qDetailsLink.GetAttribute("href").TrimStart('/');
                release.Guid = release.InfoUrl;

                var qColumns = row.QuerySelectorAll("td");
                release.Files = ParseUtil.CoerceInt(qColumns[3].TextContent);
                release.PublishDate = DateTimeUtil.FromUnknown(qColumns[5].TextContent);
                release.Size = ParseUtil.GetBytes(qColumns[6].TextContent);
                release.Grabs = ParseUtil.CoerceInt(qColumns[7].TextContent.Replace("Times", ""));
                release.Seeders = ParseUtil.CoerceInt(qColumns[8].TextContent);
                release.Peers = ParseUtil.CoerceInt(qColumns[9].TextContent) + release.Seeders;

                var qImdb = row.QuerySelector("a[href*=\"www.imdb.com\"]");
                if (qImdb != null)
                {
                    var deRefUrl = qImdb.GetAttribute("href");
                    release.ImdbId = ParseUtil.GetImdbID(WebUtility.UrlDecode(deRefUrl).Split('/').Last()) ?? 0;
                }

                release.DownloadVolumeFactor = row.QuerySelector("span.freeleech") != null ? 0 : 1;
                release.UploadVolumeFactor = 1;
                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class TorrentSeedsSettingsValidator : AbstractValidator<TorrentSeedsSettings>
    {
        public TorrentSeedsSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class TorrentSeedsSettings : IIndexerSettings
    {
        private static readonly TorrentSeedsSettingsValidator Validator = new TorrentSeedsSettingsValidator();

        public TorrentSeedsSettings()
        {
            Username = "";
            Password = "";
        }

        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "Username", HelpText = "Site Username", Privacy = PrivacyLevel.UserName)]
        public string Username { get; set; }

        [FieldDefinition(3, Label = "Password", HelpText = "Site Password", Privacy = PrivacyLevel.Password, Type = FieldType.Password)]
        public string Password { get; set; }

        [FieldDefinition(4)]
        public IndexerBaseSettings BaseSettings { get; set; } = new IndexerBaseSettings();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
