using Shokofin.API.Models;

namespace Shokofin.Configuration;

public class SeriesConfiguration {
    public SeriesType? TypeOverride { get; set; }

    public SeriesStructureType StructureType { get; set; }

    public SeriesMergingOverride MergeOverride { get; set; }

    public bool EpisodesAsSpecials { get; set; }

    public bool SpecialsAsEpisodes { get; set; }

    public bool OrderByAirdate { get; set; }
}
