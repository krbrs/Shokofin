using MediaBrowser.Model.Plugins;
using System;
using System.Text.Json.Serialization;

using TextSourceType = Shokofin.Utils.Text.TextSourceType;
using DisplayLanguageType = Shokofin.Utils.Text.DisplayLanguageType;
using SeriesAndBoxSetGroupType = Shokofin.Utils.Ordering.GroupType;
using OrderType = Shokofin.Utils.Ordering.OrderType;
using SpecialOrderType = Shokofin.Utils.Ordering.SpecialOrderType;

namespace Shokofin.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Host { get; set; }

        public string PublicHost { get; set; }

        [JsonIgnore]
        public virtual string PrettyHost
            => string.IsNullOrEmpty(PublicHost) ? Host : PublicHost;

        public string Username { get; set; }

        public string ApiKey { get; set; }

        public bool HideArtStyleTags { get; set; }

        public bool HideMiscTags { get; set; }

        public bool HidePlotTags { get; set; }

        public bool HideAniDbTags { get; set; }

        public bool HideSettingTags { get; set; }

        public bool HideProgrammingTags { get; set; }
        
        public bool HideUnverifiedTags { get; set; }

        public bool TitleAddForMultipleEpisodes { get; set; }

        public bool SynopsisCleanLinks { get; set; }

        public bool SynopsisCleanMiscLines { get; set; }

        public bool SynopsisRemoveSummary { get; set; }

        public bool SynopsisCleanMultiEmptyLines { get; set; }

        public bool AddAniDBId { get; set; }

        public bool AddTvDBId { get; set; }

        public bool AddTMDBId { get; set; }

        public TextSourceType DescriptionSource { get; set; }

        public SeriesAndBoxSetGroupType SeriesGrouping { get; set; }

        public OrderType SeasonOrdering { get; set; }

        public bool MarkSpecialsWhenGrouped { get; set; }

        public SpecialOrderType SpecialsPlacement { get; set; }

        public SeriesAndBoxSetGroupType BoxSetGrouping { get; set; }

        public OrderType MovieOrdering { get; set; }

        public bool FilterOnLibraryTypes { get; set; }

        public DisplayLanguageType TitleMainType { get; set; }

        public DisplayLanguageType TitleAlternateType { get; set; }

        /// <summary>
        /// Allow choosing any title in the selected language if no official
        /// title is available.
        /// </summary>
        public bool TitleAllowAny { get; set; }

        public UserConfiguration[] UserList { get; set; }

        public string[] IgnoredFileExtensions { get; set; }

        public string[] IgnoredFolders { get; set; }

        public bool? LibraryFilteringMode { get; set; }

        #region Experimental features

        public bool EXPERIMENTAL_EnableResolver { get; set; }

        public bool EXPERIMENTAL_AutoMergeVersions { get; set; }

        public bool EXPERIMENTAL_SplitThenMergeMovies { get; set; }

        public bool EXPERIMENTAL_SplitThenMergeEpisodes { get; set; }

        public bool EXPERIMENTAL_MergeSeasons { get; set; }

        #endregion

        public PluginConfiguration()
        {
            Host = "http://127.0.0.1:8111";
            PublicHost = "";
            Username = "Default";
            ApiKey = "";
            HideArtStyleTags = false;
            HideMiscTags = false;
            HidePlotTags = true;
            HideAniDbTags = true;
            HideSettingTags = false;
            HideProgrammingTags = true;
            HideUnverifiedTags = true;
            TitleAddForMultipleEpisodes = true;
            SynopsisCleanLinks = true;
            SynopsisCleanMiscLines = true;
            SynopsisRemoveSummary = true;
            SynopsisCleanMultiEmptyLines = true;
            AddAniDBId = true;
            AddTvDBId = true;
            AddTMDBId = true;
            TitleMainType = DisplayLanguageType.Default;
            TitleAlternateType = DisplayLanguageType.Origin;
            TitleAllowAny = false;
            DescriptionSource = TextSourceType.Default;
            SeriesGrouping = SeriesAndBoxSetGroupType.Default;
            SeasonOrdering = OrderType.Default;
            SpecialsPlacement = SpecialOrderType.AfterSeason;
            MarkSpecialsWhenGrouped = true;
            BoxSetGrouping = SeriesAndBoxSetGroupType.Default;
            MovieOrdering = OrderType.Default;
            FilterOnLibraryTypes = false;
            UserList = Array.Empty<UserConfiguration>();
            IgnoredFileExtensions  = new [] { ".nfo", ".jpg", ".jpeg", ".png" };
            IgnoredFolders = new [] { ".streams", "@recently-snapshot" };
            LibraryFilteringMode = null;
            EXPERIMENTAL_EnableResolver = false;
            EXPERIMENTAL_AutoMergeVersions = false;
            EXPERIMENTAL_SplitThenMergeMovies = true;
            EXPERIMENTAL_SplitThenMergeEpisodes = false;
            EXPERIMENTAL_MergeSeasons = false;
        }
    }
}
