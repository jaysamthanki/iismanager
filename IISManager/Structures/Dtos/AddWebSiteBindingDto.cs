namespace Techie.IISManager.Structures.Dtos
{
    /// <summary>
    /// Model for adding a new binding to a website
    /// </summary>
    public class AddWebSiteBindingDto
    {
        /// <summary>
        /// Website ID (optional if ShortName is provided)
        /// </summary>
        public long? WebSiteId { get; set; }

        /// <summary>
        /// Website short name (optional if WebSiteId is provided)
        /// </summary>
        public string? ShortName { get; set; }

        /// <summary>
        /// Hostname for the binding (e.g., example.com)
        /// </summary>
        public string HostName { get; set; } = string.Empty;

        /// <summary>
        /// Port for the binding (default: 80)
        /// </summary>
        public int Port { get; set; } = 80;

        /// <summary>
        /// Protocol for the binding (default: http)
        /// </summary>
        public string Protocol { get; set; } = "http";
    }
}
