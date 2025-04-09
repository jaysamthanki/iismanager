namespace Techie.IISManager.Api
{
    using log4net;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;
    using Structures;
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;
    using System.Threading.Tasks;
    using TypeCodes;
    using Structures.Dtos;

    /// <summary>
    /// Controller for managing certificate requests.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class CertificateController : ControllerBase
    {
        /// <summary>
        /// Logger instance for logging information and errors.
        /// </summary>
        public static ILog Log { get; } = LogManager.GetLogger(typeof(CertificateController));

        /// <summary>
        /// Retrieves all certificate requests.
        /// </summary>
        /// <returns>A list of certificate requests.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            List<dynamic> response = new List<dynamic>();

            foreach (CertificateRequest cr in CertificateRequest.GetAllRequests().OrderByDescending(a => a.DateCreated))
            {
                response.Add(new
                {
                    cr.CommonName,
                    ExpiratonDate = cr.ExpirationDate,
                    Status = cr.CertificateRequestStatus.ToString(),
                    cr.CertificateRequestId,
                    cr.DateCreated,
                    Provider = cr.CertificateAuthorityProvider.ToString(),
                    SubjectAlternativeNames = string.Join(",", cr.SubjectAlternativeNames),
                    cr.CanAutoRenew,
                });
            }

            return Ok(response);
        }

        /// <summary>
        /// Deletes a certificate request by its ID.
        /// </summary>
        /// <param name="certificateRequestId">The ID of the certificate request to delete.</param>
        /// <returns>An action result indicating the outcome of the operation.</returns>
        [HttpDelete("{certificateRequestId}")]
        public async Task<IActionResult> Delete(Guid certificateRequestId)
        {
            var certificateRequest = CertificateRequest.GetRequest(certificateRequestId);

            if (certificateRequest == null)
            {
                return BadRequest(new { status = "failed", message = "Unable to find a certificate with that id" });
            }

            await CertificateRequest.DeleteRequestAsync(certificateRequest);

            return Ok();
        }

        /// <summary>
        /// Creates a new certificate request.
        /// </summary>
        /// <param name="request">The certificate request data transfer object.</param>
        /// <returns>An action result indicating the outcome of the operation.</returns>
        [HttpPost]
        public async Task<IActionResult> Post(CertificateRequestDto request)
        {
            CertificateRequest? csr;
            List<string> subjectAltNames = new List<string>();
            dynamic response = new ExpandoObject();

            Log.DebugFormat("Input is {0}", JsonConvert.SerializeObject(request, Formatting.Indented));

            var site = SiteManager.GetWebSite(request.WebSiteId);

            if (site == null)
            {
                return BadRequest("Invalid Website");
            }

            var binding = site.SiteBindings.FirstOrDefault(a => a.WebSiteBindingId == request.WebSiteBindingId);

            if (binding == null)
            {
                return BadRequest("Invalid Website Binding");
            }

            if (request.CertificateRequest == null)
            {
                return BadRequest("Missing CSR");
            }

            csr = request.CertificateRequest;
            csr.KeyLength = 4096;
            csr.CommonName = csr.CommonName.Trim().ToLower();

            if (csr.SubjectAlternativeNames == null)
            {
                csr.SubjectAlternativeNames = new List<string>();
            }

            if (!csr.SubjectAlternativeNames.Contains(csr.CommonName))
            {
                csr.SubjectAlternativeNames.Add(csr.CommonName);
            }

            foreach (string item in csr.SubjectAlternativeNames)
            {
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                subjectAltNames.Add(item.ToLower().Trim());
            }

            csr.SubjectAlternativeNames = subjectAltNames.ToList();

            // check if we alreayd have certificates covering these domains.
            var certs = CertificateRequest.GetAllRequests();
            foreach (var cert in certs)
            {
                if (cert.ExpirationDate < DateTime.Now) continue;

                foreach (var san in cert.SubjectAlternativeNames)
                {
                    if (subjectAltNames.Contains(san))
                    {
                        subjectAltNames.Remove(san);
                    }
                }
            }

            if (subjectAltNames.Count() == 0)
            {
                return BadRequest(new { success = false, message = "Domains requested are already covered by existing active certificates" });
            }

            try
            {
                switch (csr.CertificateAuthorityProvider)
                {
                    case CertificateAuthorityProvider.LetsEncrypt:
                        csr = await LetsEncryptManager.CreateCertificate(csr, site);
                        response.success = true;
                        response.message = "Certificate created and applied";
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return Problem(ex.Message);
            }

            return Ok(response);
        }

        /// <summary>
        /// Renews an existing certificate request.
        /// </summary>
        /// <param name="certificateRequestId">The ID of the certificate request to renew.</param>
        /// <returns>An action result indicating the outcome of the operation.</returns>
        [HttpPost("{certificateRequestId}/renew")]
        public async Task<IActionResult> Renew(Guid certificateRequestId)
        {
            WebSite? site = null;
            dynamic response = new ExpandoObject();

            var certificateRequest = CertificateRequest.GetRequest(certificateRequestId);

            if (certificateRequest == null)
            {
                return NotFound("Unable to find a certificate with that id");
            }

            foreach (string domain in certificateRequest.SubjectAlternativeNames)
            {
                site = SiteManager.GetWebsiteByBinding(certificateRequest.SubjectAlternativeNames[0]);
                if (site != null)
                {
                    break;
                }
            }

            if (site == null)
            {
                return BadRequest(new { status = "failed", message = "Unable to find site in IIS to renew with this certificate" });
            }

            try
            {
                switch (certificateRequest.CertificateAuthorityProvider)
                {
                    case CertificateAuthorityProvider.LetsEncrypt:
                        certificateRequest = await LetsEncryptManager.CreateCertificate(certificateRequest, site);
                        response.success = true;
                        response.message = "Certificate created and applied";
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return Problem(ex.Message);
            }

            return Ok(response);
        }
    }
}