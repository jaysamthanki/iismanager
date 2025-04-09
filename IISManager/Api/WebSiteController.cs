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
                item = new
                {
                    site.WebSiteId,
                    site.Name,
                    site.ShortName,
                    site.PhysicalPath,
                    Bindings = new List<dynamic>()
                };

                foreach (WebSiteBinding binding in site.SiteBindings)
                {
                    item.Bindings.Add(new
                    {
                        binding.WebSiteBindingId,
                        binding.Port,
                        binding.HostName,
                        binding.Protocol,
                        IsSecured = (binding.SslFlags > 0 || binding.CertificateHash != null)
                    });
                }

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
                return BadRequest("No data provided");
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return BadRequest("Display name is required");
            }

            if (string.IsNullOrWhiteSpace(model.ShortName))
            {
                return BadRequest("Short name is required");
            }

            // Validate short name format
            if (!SiteManager.IsValidShortName(model.ShortName))
            {
                return BadRequest("Short name must contain only letters and numbers with no spaces or special characters");
            }

            // Check if site with this shortname already exists
            if (SiteManager.WebsiteShortNameExists(model.ShortName))
            {
                return BadRequest($"A website with short name '{model.ShortName}' already exists");
            }

            try
            {
                // Create the website
                WebSite newWebsite = await SiteManager.CreateWebSiteAsync(model.DisplayName, model.ShortName, model.Path);

                // Return the created website details
                dynamic response = new ExpandoObject();
                response.success = true;
                response.message = $"Website '{model.DisplayName}' created successfully";
                response.website = new
                {
                    newWebsite.WebSiteId,
                    newWebsite.Name,
                    newWebsite.ShortName,
                    newWebsite.PhysicalPath,
                    HostName = newWebsite.SiteBindings.FirstOrDefault()?.HostName,
                    Port = newWebsite.SiteBindings.FirstOrDefault()?.Port
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
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
                return BadRequest("No data provided");
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(model.HostName))
            {
                return BadRequest("Hostname is required");
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
                return NotFound("Website not found. Provide either a valid WebSiteId or ShortName.");
            }

            try
            {
                binding = await SiteManager.AddBindingAsync(targetSite, model.HostName, model.Port, model.Protocol);

                return Ok(new { success = true, message = "Binding added successfully", webSiteBindingId = binding.WebSiteBindingId });
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
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
                return Problem(ex.Message);
            }

            return Ok(new { success = true, message = "Binding removed" });
        }
    }
}