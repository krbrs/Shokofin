using System.ComponentModel.DataAnnotations;

using TitleProvider = Shokofin.Utils.Text.TitleProvider;

namespace Shokofin.Configuration;

/// <summary>
/// Titles configuration.
/// </summary>
public class TitlesConfiguration {
    /// <summary>
    /// Whether or not to use this titles configuration.
    /// </summary>
    public bool Enabled { get; set; } = false;

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
    public TitleConfiguration[] AlternateTitles { get; set; } = [new()];
}
