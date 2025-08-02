using System.ComponentModel.DataAnnotations;

using TitleProvider = Shokofin.Utils.TextUtility.TitleProvider;

namespace Shokofin.Configuration;

/// <summary>
/// Titles configuration.
/// </summary>
public class TitlesConfiguration {
    /// <summary>
    /// Remove duplicates from the alternate title list during display.
    /// </summary>
    public bool RemoveDuplicates { get; set; }

    /// <summary>
    /// The main title configuration.
    /// </summary>
    public TitleConfiguration MainTitle { get; set; } = new() {
        List = [TitleProvider.Shoko_Default],
    };

    /// <summary>
    /// The alternate title configurations.
    /// </summary>
    [MaxLength(5, ErrorMessage = "Maximum of 5 alternate titles allowed.")]
    [MinLength(1, ErrorMessage = "Minimum of 1 alternate title allowed.")]
    public TitleConfiguration[] AlternateTitles { get; set; } = [new()];
}

public class ToggleTitlesConfiguration : TitlesConfiguration {
    /// <summary>
    /// Whether or not the titles configuration is enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
