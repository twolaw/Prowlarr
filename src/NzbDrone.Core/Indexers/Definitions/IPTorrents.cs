using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using AngleSharp.Html.Parser;
using FluentValidation;
using NLog;
using NzbDrone.Common.Extensions;
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
    public class IPTorrents : TorrentIndexerBase<IPTorrentsSettings>
    {
        public override string Name => "IPTorrents";

        public override string[] IndexerUrls => new string[]
        {
            "https://iptorrents.com/",
            "https://iptorrents.me/",
            "https://nemo.iptorrents.com/",
            "https://ipt.getcrazy.me/",
            "https://ipt.findnemo.net/",
            "https://ipt.beelyrics.net/",
            "https://ipt.venom.global/",
            "https://ipt.workisboring.net/",
            "https://ipt.lol/",
            "https://ipt.cool/",
            "https://ipt.world/"
        };
        public override string Description => "IPTorrents (IPT) is a Private Torrent Tracker for 0DAY / GENERAL.";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override IndexerPrivacy Privacy => IndexerPrivacy.Private;

        public IPTorrents(IIndexerHttpClient httpClient, IEventAggregator eventAggregator, IIndexerStatusService indexerStatusService, IIndexerDefinitionUpdateService definitionService, IConfigService configService, Logger logger)
            : base(httpClient, eventAggregator, indexerStatusService, definitionService, configService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new IPTorrentsRequestGenerator() { Settings = Settings, Capabilities = Capabilities };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new IPTorrentsParser(Settings, Capabilities.Categories);
        }

        protected override IDictionary<string, string> GetCookies()
        {
            return CookieUtil.CookieHeaderToDictionary(Settings.Cookie);
        }
    }

    public class IPTorrentsRequestGenerator : IIndexerRequestGenerator
    {
        public IPTorrentsSettings Settings { get; set; }
        public IndexerCapabilities Capabilities { get; set; }

        public IPTorrentsRequestGenerator()
        {
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(string term, int[] categories, string imdbId = null)
        {
            var searchUrl = Settings.BaseUrl + "t";

            var qc = new NameValueCollection();

            if (imdbId.IsNotNullOrWhiteSpace())
            {
                // ipt uses sphinx, which supports boolean operators and grouping
                qc.Add("q", "+(" + imdbId + ")");
            }

            // changed from else if to if to support searching imdbid + season/episode in the same query
            if (!string.IsNullOrWhiteSpace(term))
            {
                // similar to above
                qc.Add("q", "+(" + term + ")");
            }

            if (Settings.FreeLeechOnly)
            {
                qc.Add("free", "on");
            }

            foreach (var cat in Capabilities.Categories.MapTorznabCapsToTrackers(categories))
            {
                qc.Add(cat, string.Empty);
            }

            searchUrl = searchUrl + "?" + qc.GetQueryString();

            var request = new IndexerRequest(searchUrl, HttpAccept.Html);

            yield return request;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetPagedRequests(string.Format("{0}", searchCriteria.SearchTerm), searchCriteria.Categories, searchCriteria.FullImdbId));

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

    public class IPTorrentsParser : IParseIndexerResponse
    {
        private readonly IPTorrentsSettings _settings;
        private readonly IndexerCapabilitiesCategories _categories;

        public IPTorrentsParser(IPTorrentsSettings settings, IndexerCapabilitiesCategories categories)
        {
            _settings = settings;
            _categories = categories;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<TorrentInfo>();

            var parser = new HtmlParser();
            var doc = parser.ParseDocument(indexerResponse.Content);

            var rows = doc.QuerySelectorAll("table[id='torrents'] > tbody > tr");
            foreach (var row in rows)
            {
                var qTitleLink = row.QuerySelector("a.hv");

                //no results
                if (qTitleLink == null)
                {
                    continue;
                }

                // drop invalid char that seems to have cropped up in some titles. #6582
                var title = qTitleLink.TextContent.Trim().Replace("\u000f", "");
                var details = new Uri(_settings.BaseUrl + qTitleLink.GetAttribute("href").TrimStart('/'));

                var qLink = row.QuerySelector("a[href^=\"/download.php/\"]");
                var link = new Uri(_settings.BaseUrl + qLink.GetAttribute("href").TrimStart('/'));

                var descrSplit = row.QuerySelector("div.sub").TextContent.Split('|');
                var dateSplit = descrSplit.Last().Split(new[] { " by " }, StringSplitOptions.None);
                var publishDate = DateTimeUtil.FromTimeAgo(dateSplit.First());
                var description = descrSplit.Length > 1 ? "Tags: " + descrSplit.First().Trim() : "";
                description += dateSplit.Length > 1 ? " Uploaded by: " + dateSplit.Last().Trim() : "";

                var catIcon = row.QuerySelector("td:nth-of-type(1) a");
                if (catIcon == null)
                {
                    // Torrents - Category column == Text or Code
                    // release.Category = MapTrackerCatDescToNewznab(row.Cq().Find("td:eq(0)").Text()); // Works for "Text" but only contains the parent category
                    throw new Exception("Please, change the 'Torrents - Category column' option to 'Icons' in the website Settings. Wait a minute (cache) and then try again.");
                }

                // Torrents - Category column == Icons
                var cat = _categories.MapTrackerCatToNewznab(catIcon.GetAttribute("href").Substring(1));

                var size = ParseUtil.GetBytes(row.Children[5].TextContent);

                var colIndex = 6;
                int? files = null;

                if (row.Children.Length == 10)
                {
                    files = ParseUtil.CoerceInt(row.Children[colIndex].TextContent.Replace("Go to files", ""));
                    colIndex++;
                }

                var grabs = ParseUtil.CoerceInt(row.Children[colIndex++].TextContent);
                var seeders = ParseUtil.CoerceInt(row.Children[colIndex++].TextContent);
                var leechers = ParseUtil.CoerceInt(row.Children[colIndex].TextContent);
                var dlVolumeFactor = row.QuerySelector("span.free") != null ? 0 : 1;

                var release = new TorrentInfo
                {
                    Title = title,
                    Guid = details.AbsoluteUri,
                    DownloadUrl = link.AbsoluteUri,
                    InfoUrl = details.AbsoluteUri,
                    PublishDate = publishDate,
                    Categories = cat,
                    Size = size,
                    Files = files,
                    Grabs = grabs,
                    Seeders = seeders,
                    Peers = seeders + leechers,
                    DownloadVolumeFactor = dlVolumeFactor,
                    UploadVolumeFactor = 1,
                    MinimumRatio = 1,
                    MinimumSeedTime = 1209600 // 336 hours
                };

                torrentInfos.Add(release);
            }

            return torrentInfos.ToArray();
        }

        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }
    }

    public class IPTorrentsSettingsValidator : AbstractValidator<IPTorrentsSettings>
    {
        public IPTorrentsSettingsValidator()
        {
            RuleFor(c => c.Cookie).NotEmpty();
        }
    }

    public class IPTorrentsSettings : IIndexerSettings
    {
        private static readonly IPTorrentsSettingsValidator Validator = new IPTorrentsSettingsValidator();

        public IPTorrentsSettings()
        {
            Cookie = "";
        }

        [FieldDefinition(1, Label = "Base Url", Type = FieldType.Select, SelectOptionsProviderAction = "getUrls", HelpText = "Select which baseurl Prowlarr will use for requests to the site")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "Cookie", HelpText = "Enter the cookie for the site. Example: `cf_clearance=0f7e7f10c62fd069323da10dcad545b828a44b6-1622730685-9-100; uid=123456789; pass=passhashwillbehere`", HelpLink = "https://wiki.servarr.com/prowlarr/faq#finding-cookies")]
        public string Cookie { get; set; }

        [FieldDefinition(3, Label = "FreeLeech Only", Type = FieldType.Checkbox, Advanced = true, HelpText = "Search Freeleech torrents only")]
        public bool FreeLeechOnly { get; set; }

        [FieldDefinition(4)]
        public IndexerBaseSettings BaseSettings { get; set; } = new IndexerBaseSettings();

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
