namespace Techie.IISManager.Structures
{
    using System;

    /// <summary>
    /// Represents a binding for a website, which includes configuration details such as hostname, port, and protocol.
    /// </summary>
    public class WebSiteBinding
    {
        /// <summary>
        /// Gets or sets the unique identifier for the web site binding.
        /// </summary>
        public Guid WebSiteBindingId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Gets or sets the host name associated with the binding.
        /// </summary>
        public string HostName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the port number used by the binding.
        /// </summary>
        public int Port { get; set; } = 80;

        /// <summary>
        /// Gets or sets the protocol (e.g., "http" or "https") for the binding.
        /// </summary>
        public string Protocol { get; set; } = "http";

        /// <summary>
        /// Gets or sets the SSL flags for the binding.
        /// </summary>
        public int SslFlags { get; set; } = 0;

        /// <summary>
        /// Gets or sets the certificate hash used for SSL bindings.
        /// A value of null indicates that no certificate is associated.
        /// </summary>
        public byte[]? CertificateHash { get; set; }
    }
}
