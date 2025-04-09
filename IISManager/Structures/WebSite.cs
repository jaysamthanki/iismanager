namespace Techie.IISManager.Structures
{
    using System;
    using System.Collections.Generic;
    using System.Xml.Serialization;

    /// <summary>
    /// Represents an IIS website configuration
    /// </summary>
    public class WebSite
    {
        /// <summary>
        /// Unique identifier for the website
        /// </summary>
        public long WebSiteId { get; set; }

        /// <summary>
        /// Display name of the website
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Physical path to the website files
        /// </summary>
        public string PhysicalPath { get; set; } = string.Empty;

        /// <summary>
        /// Short customer/site name used for app pool and folder name
        /// </summary>
        public string ShortName { get; set; } = string.Empty;

        /// <summary>
        /// Bindings associated with this website
        /// </summary>
        public List<WebSiteBinding> SiteBindings { get; set; }

        /// <summary>
        /// Constructor initializes default values
        /// </summary>
        public WebSite()
        {
            SiteBindings = new List<WebSiteBinding>();
        }
    }
}