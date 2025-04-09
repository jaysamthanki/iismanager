using System;

namespace Techie.IISManager.Structures.Dtos
{
    /// <summary>
    /// Request model for removing a website binding
    /// </summary>
    public class RemoveWebSiteBindingRequestDto
    {
        /// <summary>
        /// Gets or sets the ID of the website
        /// </summary>
        public long WebSiteId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the website binding (optional)
        /// </summary>
        public Guid? WebSiteBindingId { get; set; }

        /// <summary>
        /// Gets or sets the hostname of the binding (optional)
        /// </summary>
        public string? HostName { get; set; }

        /// <summary>
        /// Gets or sets the protocol of the binding (default: http)
        /// </summary>
        public string Protocol { get; set; } = "http";

        /// <summary>
        /// Gets or sets the port of the binding (default: 80)
        /// </summary>
        public int Port { get; set; } = 80;
    }
}
