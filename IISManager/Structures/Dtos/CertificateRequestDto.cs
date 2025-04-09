using System;

namespace Techie.IISManager.Structures.Dtos
{
    /// <summary>
    /// Data transfer object containing the necessary details for a certificate request 
    /// associated with a specific website binding.
    /// </summary>
    public class CertificateRequestDto
    {
        /// <summary>
        /// Gets or sets the identifier of the website for which the certificate is requested.
        /// </summary>
        public long WebSiteId { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier of the website binding associated with the certificate request.
        /// </summary>
        public Guid WebSiteBindingId { get; set; }

        /// <summary>
        /// Gets or sets the certificate request details including certificate, key, and related properties.
        /// </summary>
        public CertificateRequest? CertificateRequest { get; set; }
    }
}
