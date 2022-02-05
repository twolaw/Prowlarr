using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using FluentValidation;
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
    public class AnimeTorrents : TorrentIndexerBase<AnimeTorrentsSettings>
    {
        public override string Name => "AnimeTorrents";

        public override string[] IndexerUrls => new string[] { "https://animetorrents.me/" };
        public override string Description => "Definitive source for anime and manga";
        private string LoginUrl => Settings.BaseUrl + "login.php";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public AnimeTorrents(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new AnimeTorrentsRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new AnimeTorrentsParser(Settings, Capabilities.Categories);
        }

        protected override async Task DoLogin()
        {
            UpdateCookies(null, null);

            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true,
                AllowAutoRedirect = true
            };

            var loginPage = await ExecuteAuth(new HttpRequest(LoginUrl));
            requestBuilder.Method = HttpMethod.Post;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);
            requestBuilder.SetCookies(loginPage.GetCookies());

            var authLoginRequest = requestBuilder
                .AddFormParameter("username", Settings.Username)
                .AddFormParameter("password", Settings.Password)
                .AddFormParameter("form", "login")
                .AddFormParameter("rememberme[]", "1")
                .SetHeader("Content-Type", "multipart/form-data")
                .Build();

            var response = await ExecuteAuth(authLoginRequest);

            if (response.Content != null && response.Content.Contains("logout.php"))
            {
                UpdateCookies(response.GetCookies(), DateTime.Now + TimeSpan.FromDays(30));

                _logger.Debug("AnimeTorrents authentication succeeded");
            }
            else
            {
                throw new IndexerAuthException("AnimeTorrents authentication failed");
            }
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            if (httpResponse.Content.Contains("Access Denied!") || httpResponse.Content.Contains("login.php"))
            {
                return true;
            }

            return false;
        }
    }

    public class AnimeTorrentsRequestGenerator : IIndexerRequestGenerator
    {
        public AnimeTorrentsSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        public AnimeTorrentsRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories)
        {
            var searchString = term;

            //  replace any space, special char, etc. with % (wildcard)
            var replaceRegex = new Regex("[^a-zA-Z0-9]+");
            searchString = replaceRegex.Replace(searchString, "%");
            var searchUrl = Settings.BaseUrl + "ajax/torrents_data.php";
            var searchUrlReferer = Settings.BaseUrl + "torrents.php?cat=0&searchin=filename&search=";

            var trackerCats = Capabilities.Categories.MapTorznabCapsToTrackers(categories) ?? new List<string>();

            var queryCollection = new NameValueCollection
            {
                { "total", "146" }, // Not sure what this is about but its required!
                { "cat", trackerCats.Count == 1 ? trackerCats.First() : "0" },
                { "page", "1" },
                { "searchin", "filename" },
                { "search", searchString }
            };

            searchUrl += "?" + queryCollection.GetQueryString();

            var extraHeaders = new NameValueCollection
            {
                { "X-Requested-With", "XMLHttpRequest" },
                { "Referer", searchUrlReferer }
            };

            var request = new IndexerRequest(searchUrl, null);
            request.HttpRequest.Headers.Add(extraHeaders);

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

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SanitizedSearchTerm), searchCriteria.Categories));

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

    public class AnimeTorrentsParser : IParseIndexerResponse
    {
        private readonly AnimeTorrentsSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public AnimeTorrentsParser(AnimeTorrentsSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<TorrentInfo>();

            var parser = new HtmlParser();
            var dom = parser.ParseDocument(indexerResponse.Content);

            var rows = dom.QuerySelectorAll("tr");
            foreach (var row in rows.Skip(1))
            {
                var release = new TorrentInfo();
                var qTitleLink = row.QuerySelector("td:nth-of-type(2) a:nth-of-type(1)");
                release.Title = qTitleLink.TextContent.Trim();

                // If we search an get no results, we still get a table just with no info.
                if (string.IsNullOrWhiteSpace(release.Title))
                {
                    break;
                }

                release.Guid = qTitleLink.GetAttribute("href");
                release.InfoUrl = release.Guid;

                var dateString = row.QuerySelector("td:nth-of-type(5)").TextContent;
                release.PublishDate = DateTime.ParseExact(dateString, "dd MMM yy", CultureInfo.InvariantCulture);

                // newbie users don't see DL links
                var qLink = row.QuerySelector("td:nth-of-type(3) a");
                if (qLink != null)
                {
                    release.DownloadUrl = qLink.GetAttribute("href");
                }
                else
                {
                    // use details link as placeholder
                    // null causes errors during export to torznab
                    // skipping the release prevents newbie users from adding the tracker (empty result)
                    release.DownloadUrl = release.InfoUrl;
                }

                var sizeStr = row.QuerySelector("td:nth-of-type(6)").TextContent;
                release.Size = ParseUtil.GetBytes(sizeStr);

                var connections = row.QuerySelector("td:nth-of-type(8)").TextContent.Trim().Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                release.Seeders = ParseUtil.CoerceInt(connections[0].Trim());
                release.Peers = ParseUtil.CoerceInt(connections[1].Trim()) + release.Seeders;
                release.Grabs = ParseUtil.CoerceInt(connections[2].Trim());

                var rCat = row.QuerySelector("td:nth-of-type(1) a").GetAttribute("href");
                var rCatIdx = rCat.IndexOf("cat=");
                if (rCatIdx > -1)
                {
                    rCat = rCat.Substring(rCatIdx + 4);
                }

                release.Categories = _categories.MapTrackerCatToNewznab(rCat);

                if (row.QuerySelector("img[alt=\"Gold Torrent\"]") != null)
                {
                    release.DownloadVolumeFactor = 0;
                }
                else if (row.QuerySelector("img[alt=\"Silver Torrent\"]") != null)
                {
                    release.DownloadVolumeFactor = 0.5;
                }
                else
                {
                    release.DownloadVolumeFactor = 1;
                }

                var uLFactorImg = row.QuerySelector("img[alt*=\"x Multiplier Torrent\"]");
                if (uLFactorImg != null)
                {
                    release.UploadVolumeFactor = ParseUtil.CoerceDouble(uLFactorImg.GetAttribute("alt").Split('x')[0]);
                }
                else
                {
                    release.UploadVolumeFactor = 1;
                }

                qTitleLink.Remove();

                //release.Description = row.QuerySelector("td:nth-of-type(2)").TextContent;
                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class AnimeTorrentsSettingsValidator : AbstractValidator<AnimeTorrentsSettings>
    {
        public AnimeTorrentsSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class AnimeTorrentsSettings : IIndexerSettings
    {
        private static readonly AnimeTorrentsSettingsValidator Validator = new AnimeTorrentsSettingsValidator();

        public AnimeTorrentsSettings()
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
