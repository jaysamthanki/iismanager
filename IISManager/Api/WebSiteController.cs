namespace Techie.IISManager.Api
{
    using Microsoft.AspNetCore.Mvc;
    using Structures;
    using Structures.Dtos;
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Controller for managing websites in IIS
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class WebSiteController : ControllerBase
    {
        /// <summary>
        /// Gets all websites
        /// </summary>
        /// <param name="clearcache">Whether to clear cache before fetching sites</param>
        /// <returns>List of all websites</returns>
        [HttpGet]
        public async Task<IActionResult> Get(bool clearcache = false)
        {
            List<dynamic> response = new List<dynamic>();
            dynamic item;

            if (clearcache == true)
            {
                await SiteManager.ClearCacheAsync();
            }

            foreach (WebSite site in SiteManager.GetWebsites().OrderBy(a => a.Name))
            {
                List<dynamic> bindings = [];

                foreach (WebSiteBinding binding in site.SiteBindings.OrderBy(a => a.HostName))
                {
                    bindings.Add(new
                    {
                        binding.WebSiteBindingId,
                        binding.Port,
                        binding.HostName,
                        binding.Protocol,
                        IsSecured = (binding.SslFlags > 0 || binding.CertificateHash != null)
                    });
                }

                item = new
                {
                    site.WebSiteId,
                    site.Name,
                    site.ShortName,
                    site.PhysicalPath,
                    Bindings = bindings,
                    BindingCount = bindings.Count()
                };

                response.Add(item);
            }

            return Ok(response);
        }

        /// <summary>
        /// Creates a new website in IIS
        /// </summary>
        /// <param name="model">Website creation model</param>
        /// <returns>Created website details</returns>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AddWebSiteDto model)
        {
            if (model == null)
            {
                return Problem("No data provided", statusCode: 400);
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return Problem("Display name is required", statusCode: 400);
            }

            if (string.IsNullOrWhiteSpace(model.ShortName))
            {
                return Problem("Short name is required", statusCode: 400);
            }

            // Validate short name format
            if (!SiteManager.IsValidShortName(model.ShortName))
            {
                return Problem("Short name must contain only letters and numbers with no spaces or special characters", statusCode: 400);
            }

            // Check if site with this shortname already exists
            if (SiteManager.WebsiteShortNameExists(model.ShortName))
            {
                return Problem($"A website with short name '{model.ShortName}' already exists", statusCode: 400);
            }

            // CHeck if a site with this displayname already exists
            if (SiteManager.WebsiteDisplayNameExists(model.DisplayName))
            {
                return Problem($"A website with display name '{model.DisplayName}' already exists", statusCode: 400);
            }

            try
            {
                // Create the website
                WebSite newWebsite = await SiteManager.CreateWebSiteAsync(model.DisplayName, model.ShortName, model.PhysicalPath);

                return Ok(new
                {
                    newWebsite.WebSiteId,
                    newWebsite.Name,
                    newWebsite.ShortName,
                    newWebsite.PhysicalPath,
                    HostName = newWebsite.SiteBindings.FirstOrDefault()?.HostName,
                    Port = newWebsite.SiteBindings.FirstOrDefault()?.Port
                });
            }
            catch (Exception ex)
            {
                return Problem(ex.Message, statusCode: 400);
            }
        }

        /// <summary>
        /// Gets a specific website by ID
        /// </summary>
        /// <param name="id">Website ID</param>
        /// <returns>Website details</returns>
        [HttpGet("{id}")]
        public IActionResult GetById(long id)
        {
            WebSite? site = SiteManager.GetWebSite(id);

            if (site == null)
            {
                return NotFound($"Website with ID {id} not found");
            }

            dynamic response = new
            {
                site.WebSiteId,
                site.Name,
                site.ShortName,
                site.PhysicalPath,
                Bindings = site.SiteBindings.Select(b => new
                {
                    b.WebSiteBindingId,
                    b.Port,
                    b.HostName,
                    b.Protocol,
                    IsSecured = (b.SslFlags > 0 || b.CertificateHash != null)
                }).ToList()
            };

            return Ok(response);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("{id}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateWebSiteDto request)
        {
            WebSite? site = SiteManager.GetWebSite(id);

            if (site == null)
            {
                return Problem(detail: $"Website with ID {id} not found", statusCode: 404);
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return Problem("Display name is required", statusCode: 400);
            }

            if (string.IsNullOrEmpty(request.PhysicalPath))
            {
                return Problem("Path is required", statusCode: 400);
            }

            // Check if a site with this displayname already exists
            if (SiteManager.WebsiteDisplayNameExists(request.DisplayName, id))
            {
                return Problem("A website with display name '{request.DisplayName}' already exists", statusCode: 400);
            }

            try
            {
                // Update the website
                await SiteManager.UpdateWebSiteAsync(site, request.DisplayName, request.PhysicalPath);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message, statusCode: 400);
            }

            return Ok();
        }

        /// <summary>
        /// Adds a new hostname binding to an existing website
        /// </summary>
        /// <param name="model">Model containing binding details</param>
        /// <returns>Result of the binding operation</returns>
        [HttpPost("binding")]
        public async Task<IActionResult> AddBinding([FromBody] AddWebSiteBindingDto model)
        {
            WebSite? targetSite = null;
            WebSiteBinding? binding = null;

            if (model == null)
            {
                return Problem("No data provided", statusCode: 400);
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(model.HostName))
            {
                return Problem("Host name is required", statusCode: 400);
            }

            // Find site by ID or shortname
            if (model.WebSiteId.HasValue)
            {
                targetSite = SiteManager.GetWebSite(model.WebSiteId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(model.ShortName))
            {
                targetSite = SiteManager.GetWebSite(model.ShortName);
            }

            if (targetSite == null)
            {
                return Problem("Website not found. Provide either a valid WebSiteId or ShortName.", statusCode: 404);
            }

            try
            {
                binding = await SiteManager.AddBindingAsync(targetSite, model.HostName, model.Port, model.Protocol);

                return Ok(new 
                { 
                    success = true, 
                    message = "Binding added successfully", 
                    webSiteBindingId = binding.WebSiteBindingId,
                    binding.Port,
                    binding.HostName,
                    binding.Protocol,
                    IsSecured = (binding.SslFlags > 0 || binding.CertificateHash != null)
                });
            }
            catch (Exception ex)
            {
                return Problem(ex.Message, statusCode: 400);
            }
        }

        /// <summary>
        /// Removes a website binding from a website
        /// </summary>
        /// <param name="request">WebSiteID is required. Provide either websitebindingId, or the host/protocol/port combination of the binding to remove.</param>
        /// <returns></returns>
        [HttpDelete("binding")]
        public async Task<IActionResult> RemoveBinding([FromBody] RemoveWebSiteBindingRequestDto request)
        {
            WebSite? site = SiteManager.GetWebSite(request.WebSiteId);
            WebSiteBinding? binding = null;

            if (site == null)
            {
                return NotFound($"Website with ID {request.WebSiteId} not found");
            }
            
            if (request.WebSiteBindingId.HasValue)
            {
                binding = site.SiteBindings.FirstOrDefault(b => b.WebSiteBindingId == request.WebSiteBindingId);
            }
            else if (!string.IsNullOrWhiteSpace(request.HostName))
            {
                binding = site.SiteBindings.FirstOrDefault(b => b.HostName == request.HostName && b.Port == request.Port && b.Protocol == request.Protocol);
            }

            if (binding == null)
            {
                return NotFound("Binding not found");
            }

            try
            {
                await SiteManager.RemoveWebSiteBindingAsync(site, binding.HostName, binding.Protocol, binding.Port);
            }
            catch (Exception ex)
            {
                Global.Log.Error(ex);
                return Problem(ex.Message, statusCode: 400);
            }

            return Ok(new { success = true, message = "Binding removed" });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("securebindings")]
        public async Task<IActionResult> SecureBindings([FromBody] SecureBindingRequestDto request)
        {
            WebSite? site = SiteManager.GetWebSite(request.WebSiteId);
            List<WebSiteBinding> bindings = new List<WebSiteBinding>();
            CertificateRequest csr;
            List<dynamic> responsebinding;

            if (site == null)
            {
                return Problem($"Website with ID {request.WebSiteId} not found", statusCode: 400);
            }

            csr = new CertificateRequest();

            foreach (var item in request.WebSiteBindingIds)
            {
                var binding = site.SiteBindings.FirstOrDefault(b => b.WebSiteBindingId == item);
                if (binding != null)
                {
                    bindings.Add(binding);
                    csr.SubjectAlternativeNames.Add(binding.HostName.ToLower());
                }
            }

            if (bindings.Count == 0)
            {
                return Problem("No bindings found to secure", statusCode: 400);
            }

            csr.CanAutoRenew = true;
            csr.CertificateAuthorityProvider = TypeCodes.CertificateAuthorityProvider.LetsEncrypt;
            csr.CommonName = bindings[0].HostName.ToLower();
            csr.KeyLength = 4096;

            // check if we alreayd have certificates covering these domains.
            var certs = CertificateRequest.GetAllRequests();
            foreach (var cert in certs)
            {
                if (cert.ExpirationDate < DateTime.Now) continue;

                foreach (var san in cert.SubjectAlternativeNames)
                {
                    if (csr.SubjectAlternativeNames.Contains(san))
                    {
                        csr.SubjectAlternativeNames.Remove(san);
                    }
                }
            }

            if (csr.SubjectAlternativeNames.Count == 0)
            {
                return Problem("No new domains to secure", statusCode: 400);
            }

            try
            {
                csr = await LetsEncryptManager.CreateCertificate(csr, site);
            }
            catch (Exception ex)
            {
                Global.Log.Error(ex);
                return Problem(ex.Message, statusCode: 400);
            }

            responsebinding = [];

            site = SiteManager.GetWebSite(request.WebSiteId);
            foreach (var binding in site!.SiteBindings)
            {
                responsebinding.Add(new
                {
                    binding.WebSiteBindingId,
                    binding.Port,
                    binding.HostName,
                    binding.Protocol,
                    IsSecured = (binding.SslFlags > 0 || binding.CertificateHash != null)
                });
            }

            return Ok(new
            {
                csr.CertificateRequestId,
                csr.SubjectAlternativeNames,
                csr.ExpirationDate,
                bindings = responsebinding
            });
        }
    }
}