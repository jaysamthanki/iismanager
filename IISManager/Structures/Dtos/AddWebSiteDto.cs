namespace Techie.IISManager.Structures.Dtos
{
    /// <summary>
    /// Model for creating a new website
    /// </summary>
    public class AddWebSiteDto
    {
        /// <summary>
        /// Display name for the website
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Short name for the website (alphanumeric only)
        /// </summary>
        public string ShortName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path for the new website
        /// </summary>
        public string? Path { get; set; }
    }
}
