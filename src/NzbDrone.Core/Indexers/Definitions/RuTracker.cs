using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
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
    public class RuTracker : TorrentIndexerBase<RuTrackerSettings>
    {
        public override string Name => "RuTracker";
        public override string[] IndexerUrls => new string[] { "https://rutracker.org/", "https://rutracker.net/" };

        private string LoginUrl => Settings.BaseUrl + "forum/login.php";
        public override string Description => "RuTracker is a Semi-Private Russian torrent site with a thriving file-sharing community";
        public override string Language => "ru-org";
        public override Encoding Encoding => Encoding.GetEncoding("windows-1251");
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.SemiPrivate;

        public RuTracker(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new RuTrackerRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new RuTrackerParser(Settings, Capabilities.Categories);
        }

        protected override async Task DoLogin()
        {
            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true,
                AllowAutoRedirect = true
            };

            requestBuilder.Method = HttpMethod.Post;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);

            var cookies = Cookies;

            Cookies = null;
            requestBuilder.AddFormParameter("login_username", Settings.Username)
                .AddFormParameter("login_password", Settings.Password)
                .AddFormParameter("login", "Login")
                .SetHeader("Content-Type", "multipart/form-data");

            var authLoginRequest = requestBuilder.Build();

            var response = await ExecuteAuth(authLoginRequest);

            if (!response.Content.Contains("id=\"logged-in-username\""))
            {
                throw new IndexerAuthException("RuTracker Auth Failed");
            }

            cookies = response.GetCookies();
            UpdateCookies(cookies, DateTime.Now + TimeSpan.FromDays(30));

            _logger.Debug("RuTracker authentication succeeded.");
        }

        protected override bool CheckIfLoginNeeded(HttpResponse httpResponse)
        {
            if (httpResponse.RedirectUrl.Contains("login.php") || !httpResponse.Content.Contains("id=\"logged-in-username\""))
            {
                return true;
            }

            return false;
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getUrls")
            {
                var links = IndexerUrls;

                return new
                {
                    options = links.Select(d => new { Value = d, Name = d })
                };
            }

            return null;
        }
    }

    public class RuTrackerRequestGenerator : IIndexerRequestGenerator
    {
        public RuTrackerSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        public RuTrackerRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories, int season = 0)
        {
            var searchUrl = string.Format("{0}/forum/tracker.php", Settings.BaseUrl.TrimEnd('/'));

            var queryCollection = new NameValueCollection();

            var searchString = term;

            // if the search string is empty use the getnew view
            if (string.IsNullOrWhiteSpace(searchString))
            {
                queryCollection.Add("nm", searchString);
            }
            else
            {
                // use the normal search
                searchString = searchString.Replace("-", " ");
                if (season != 0)
                {
                    searchString += " Сезон: " + season;
                }

                queryCollection.Add("nm", searchString);
            }

            if (categories != null && categories.Length > 0)
            {
                queryCollection.Add("f", string.Join(",", Capabilities.Categories.MapTorznabCapsToTrackers(categories)));
            }

            searchUrl = searchUrl + "?" + queryCollection.GetQueryString();

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

            if (searchCriteria.Season == null)
            {
                searchCriteria.Season = 0;
            }

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

    public class RuTrackerParser : IParseIndexerResponse
    {
        private readonly RuTrackerSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public RuTrackerParser(RuTrackerSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var parser = new HtmlParser();
            var doc = parser.ParseDocument(indexerResponse.Content);
            var rows = doc.QuerySelectorAll("table#tor-tbl > tbody > tr");

            foreach (var row in rows)
            {
                var release = ParseReleaseRow(row);
                if (release != null)
                {
                    torrentInfos.Add(release);
                }
            }

            return torrentInfos.ToArray();
        }

        private TorrentInfo ParseReleaseRow(IElement row)
        {
            var qDownloadLink = row.QuerySelector("td.tor-size > a.tr-dl");

            // Expects moderation
            if (qDownloadLink == null)
            {
                return null;
            }

            var link = _settings.BaseUrl + "forum/" + qDownloadLink.GetAttribute("href");

            var qDetailsLink = row.QuerySelector("td.t-title-col > div.t-title > a.tLink");
            var details = _settings.BaseUrl + "forum/" + qDetailsLink.GetAttribute("href");

            var category = GetCategoryOfRelease(row);

            var size = GetSizeOfRelease(row);

            var seeders = GetSeedersOfRelease(row);
            var leechers = ParseUtil.CoerceInt(row.QuerySelector("td:nth-child(8)").TextContent);

            var grabs = ParseUtil.CoerceInt(row.QuerySelector("td:nth-child(9)").TextContent);

            var publishDate = GetPublishDateOfRelease(row);

            var release = new TorrentInfo
            {
                MinimumRatio = 1,
                MinimumSeedTime = 0,
                Title = qDetailsLink.TextContent,
                InfoUrl = details,
                DownloadUrl = link,
                Guid = details,
                Size = size,
                Seeders = seeders,
                Peers = leechers + seeders,
                Grabs = grabs,
                PublishDate = publishDate,
                Categories = category,
                DownloadVolumeFactor = 1,
                UploadVolumeFactor = 1
            };

            // TODO finish extracting release variables to simplify release initialization
            if (IsAnyTvCategory(release.Categories))
            {
                // extract season and episodes
                // should also handle multi-season releases listed as Сезон: 1-8 and Сезоны: 1-8
                var regex = new Regex(@".+\/\s([^а-яА-я\/]+)\s\/.+Сезон.\s*[:]*\s+(\d*\-?\d*).+(?:Серии|Эпизод)+\s*[:]*\s+(\d+-?\d*).+(\[.*\])[\s]?(.*)");

                var title = regex.Replace(release.Title, "$1 - S$2E$3 - rus $4 $5");
                title = Regex.Replace(title, "-Rip", "Rip", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, "WEB-DLRip", "WEBDL", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, "WEB-DL", "WEBDL", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, "HDTVRip", "HDTV", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, "Кураж-Бамбей", "kurazh", RegexOptions.IgnoreCase);

                release.Title = title;
            }
            else if (IsAnyMovieCategory(release.Categories))
            {
                // Bluray quality fix: radarr parse Blu-ray Disc as Bluray-1080p but should be BR-DISK
                release.Title = Regex.Replace(release.Title, "Blu-ray Disc", "BR-DISK", RegexOptions.IgnoreCase);
            }

            if (IsAnyTvCategory(release.Categories) | IsAnyMovieCategory(release.Categories))
            {
                                // remove director's name from title
                // rutracker movies titles look like: russian name / english name (russian director / english director) other stuff
                // Ирландец / The Irishman (Мартин Скорсезе / Martin Scorsese) [2019, США, криминал, драма, биография, WEB-DL 1080p] Dub (Пифагор) + MVO (Jaskier) + AVO (Юрий Сербин) + Sub Rus, Eng + Original Eng
                // this part should be removed: (Мартин Скорсезе / Martin Scorsese)
                //var director = new Regex(@"(\([А-Яа-яЁё\W]+)\s/\s(.+?)\)");
                var director = new Regex(@"(\([А-Яа-яЁё\W].+?\))");
                release.Title = director.Replace(release.Title, "");

                // Remove VO, MVO and DVO from titles
                var vo = new Regex(@".VO\s\(.+?\)");
                release.Title = vo.Replace(release.Title, "");

                // Remove R5 and (R5) from release names
                var r5 = new Regex(@"(.*)(.R5.)(.*)");
                release.Title = r5.Replace(release.Title, "$1");

                // Remove Sub languages from release names
                var sub = new Regex(@"(Sub.*\+)|(Sub.*$)");
                release.Title = sub.Replace(release.Title, "");

                // language fix: all rutracker releases contains russian track
                if (release.Title.IndexOf("rus", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    release.Title += " rus";
                }

                // remove russian letters
                if (_settings.RussianLetters == true)
                {
                    //Strip russian letters
                    var rusRegex = new Regex(@"(\([А-Яа-яЁё\W]+\))|(^[А-Яа-яЁё\W\d]+\/ )|([а-яА-ЯЁё \-]+,+)|([а-яА-ЯЁё]+)");

                    release.Title = rusRegex.Replace(release.Title, "");

                    // Replace everything after first forward slash with a year (to avoid filtering away releases with an fwdslash after title+year, like: Title Year [stuff / stuff])
                    var fwdslashRegex = new Regex(@"(\/\s.+?\[)");
                    release.Title = fwdslashRegex.Replace(release.Title, "[");
                }
            }

            return release;
        }

        private int GetSeedersOfRelease(in IElement row)
        {
            var seeders = 0;
            var qSeeders = row.QuerySelector("td:nth-child(7)");
            if (qSeeders != null && !qSeeders.TextContent.Contains("дн"))
            {
                var seedersString = qSeeders.QuerySelector("b").TextContent;
                if (!string.IsNullOrWhiteSpace(seedersString))
                {
                    seeders = ParseUtil.CoerceInt(seedersString);
                }
            }

            return seeders;
        }

        private ICollection<IndexerCategory> GetCategoryOfRelease(in IElement row)
        {
            var forum = row.QuerySelector("td.f-name-col > div.f-name > a");
            var forumid = forum.GetAttribute("href").Split('=')[1];
            return _categories.MapTrackerCatToNewznab(forumid);
        }

        private long GetSizeOfRelease(in IElement row)
        {
            var qSize = row.QuerySelector("td.tor-size");
            var size = ParseUtil.GetBytes(qSize.GetAttribute("data-ts_text"));
            return size;
        }

        private DateTime GetPublishDateOfRelease(in IElement row)
        {
            var timestr = row.QuerySelector("td:nth-child(10)").GetAttribute("data-ts_text");
            var publishDate = DateTimeUtil.UnixTimestampToDateTime(long.Parse(timestr));
            return publishDate;
        }

        private bool IsAnyTvCategory(ICollection<IndexerCategory> category)
        {
            return category.Contains(NewznabStandardCategory.TV)
                || NewznabStandardCategory.TV.SubCategories.Any(subCat => category.Contains(subCat));
        }

        private bool IsAnyMovieCategory(ICollection<IndexerCategory> category)
        {
            return category.Contains(NewznabStandardCategory.Movies)
                || NewznabStandardCategory.Movies.SubCategories.Any(subCat => category.Contains(subCat));
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class RuTrackerSettingsValidator : AbstractValidator<RuTrackerSettings>
    {
        public RuTrackerSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.Password).NotEmpty();
        }
    }

    public class RuTrackerSettings : IIndexerSettings
    {
        private static readonly RuTrackerSettingsValidator Validator = new RuTrackerSettingsValidator();

        public RuTrackerSettings()
        {
            Username = "";
            Password = "";
            RussianLetters = false;
        }

        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "Username", Advanced = false, HelpText = "Site Username")]
        public string Username { get; set; }

        [FieldDefinition(3, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Site Password")]
        public string Password { get; set; }

        [FieldDefinition(4, Label = "Strip Russian letters", Type = FieldType.Checkbox, SelectOptionsProviderAction = "stripRussian", HelpText = "Removes russian letters")]
        public bool RussianLetters { get; set; }

        [FieldDefinition(5)]
        public IndexerBaseSettings BaseSettings { get; set; } = new IndexerBaseSettings();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
