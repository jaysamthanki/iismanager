namespace Techie.IISManager
{
    using log4net;
    using Microsoft.Web.Administration;
    using Microsoft.Win32;
    using Newtonsoft.Json;
    using Structures;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Xml.Schema;
    using TypeCodes;

    /// <summary>
    /// Manages IIS websites and their configurations
    /// </summary>
    public class SiteManager
    {
        /// <summary>
        /// Logger for the SiteManager class
        /// </summary>
        public static ILog Log { get; set; }

        /// <summary>
        /// Cache of websites from IIS
        /// </summary>
        private static List<WebSite> sites;

        /// <summary>
        /// Location for centralized certificate store
        /// </summary>
        private static string centralizedConfigurationStoreLocation = string.Empty;

        /// <summary>
        /// Initialize the SiteManager
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
        static SiteManager()
        {
            Log = LogManager.GetLogger(typeof(SiteManager));

            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\IIS\CentralCertProvider"))
            {
                if (key != null)
                {
                    if (key.GetValue("Enabled") != null && (int?)key.GetValue("Enabled") == 1)
                    {
                        centralizedConfigurationStoreLocation = (string)key.GetValue("CertStoreLocation")!;
                    }
                }
            }

            if (File.Exists(@".\Configuration\WebSites.json"))
            {
                sites = JsonConvert.DeserializeObject<List<WebSite>>(File.ReadAllText(@".\Configuration\WebSites.json"))!;
            }
            else
            {
                // prime the list.
                sites = GetWebsites();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="website"></param>
        /// <param name="hostName"></param>
        /// <param name="port"></param>
        /// <param name="protocol"></param>
        /// <exception cref="ApplicationException"></exception>
        public async static ValueTask<WebSiteBinding> AddBindingAsync(WebSite website, string hostName, int port, string protocol)
        {
            ServerManager sm = new ServerManager();
            var site = sm.Sites.FirstOrDefault(s => s.Id == website.WebSiteId);
            WebSiteBinding webSiteBinding;

            if (site == null)
            {
                throw new ApplicationException($"Could not find website with ID {website.WebSiteId}");
            }

            await ClearCacheAsync();

            // check all sites to make sure we arent duplicating the binding
            foreach (WebSite s in GetWebsites())
            {
                if (s.SiteBindings.Any(a => a.HostName.Equals(hostName, StringComparison.OrdinalIgnoreCase) && a.Port == port))
                {
                    throw new ApplicationException($"A binding for {hostName} already exists");
                }
            }

            var binding = site.Bindings.Add($"*:{port}:{hostName}", protocol);

            if (protocol == "https")
            {
                binding["sslFlags"] = 3; // SNI + CCS
            }
            
            try
            {
                sm.CommitChanges();
                Log.InfoFormat("Website '{0}' successfully created with binding {1}", website.Name, hostName);
            }
            catch (Exception ex)
            {
                Log.Error($"Error committing website changes: {ex.Message}", ex);
                throw;
            }

            webSiteBinding = new WebSiteBinding
            {
                HostName = hostName,
                Port = port,
                Protocol = protocol
            };


            website.SiteBindings.Add(webSiteBinding);

            await SaveDatabaseAsync();

            return webSiteBinding;
        }

        /// <summary>
        /// Bind a certificate to website(s)
        /// </summary>
        /// <param name="cert">Certificate request to bind</param>
        /// <param name="pfx">Certificate data</param>
        public async static ValueTask<CertificateRequest> BindCertificateAsync(CertificateRequest cert, byte[] pfx)
        {
            ServerManager sm;

            Log.Info($"Attempting to bind certificate for CertificateRequestId {cert.CertificateRequestId}");

            sm = new ServerManager();

            foreach (string domain in cert.SubjectAlternativeNames)
            {
                Log.Info($"Looking for site a site with hostname {domain}");
                
                var site = GetWebsiteByBinding(sm, domain);

                if (site == null)
                {
                    throw new ApplicationException(string.Format("Could not find a site with a binding of {0} in IIS", domain));
                }

                if (!string.IsNullOrEmpty(centralizedConfigurationStoreLocation))
                {
                    var path = Path.Combine(centralizedConfigurationStoreLocation, domain + ".pfx");
                    Log.DebugFormat("Writing pfx to {0}", path);
                    await File.WriteAllBytesAsync(path, pfx);
                }

                try
                {
                    // What if there is a binding already?
                    var binding = site.Bindings.FirstOrDefault(a => a.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) && a.Protocol == "https" && (int)a.SslFlags == 3);

                    if (binding != null)
                    {
                        Log.Info($"Site {site.Name} already has correct binding for {domain}");
                        continue;
                    }

                    binding = site.Bindings.FirstOrDefault(a => a.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) && a.Protocol == "https");
                    if (binding != null)
                    {
                        // its bound but using non Centralized ssl.
                        Log.Info($"Site {site.Name} already has a binding for {domain} but not using SNI + CCS");
                        site.Bindings.Remove(binding);
                    }

                    binding = site.Bindings.Add(string.Format("*:443:{0}", domain), "https");
                    
                    // Set ssl flags to "3", for SNI + CCS
                    binding["sslFlags"] = 3;

                    sm.CommitChanges();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            cert.CertificateRequestStatus = CertificateRequestStatus.Completed;

            await ClearCacheAsync();

            return cert;
        }

        /// <summary>
        /// Clear the website cache
        /// </summary>
        public async static ValueTask ClearCacheAsync()
        {
            ServerManager sm = new ServerManager();
            List<WebSite> newList = sm.Sites.Select(a => new WebSite
            {
                Name = a.Name,
                PhysicalPath = a.Applications["/"].VirtualDirectories["/"].PhysicalPath,
                WebSiteId = a.Id,
                SiteBindings = a.Bindings.Select(b => new WebSiteBinding
                {
                    HostName = b.Host,
                    Port = b.EndPoint.Port,
                    Protocol = b.Protocol,
                    SslFlags = (int)b.SslFlags,
                    CertificateHash = b.CertificateHash
                }).ToList()
            }).ToList();

            Log.Info("Clearing website cache");

            if (sites == null)
            {
                sites = [];
            }

            foreach (var site in newList)
            {
                // check the sites list for an existing site with the smae id.. if it exists, then update the name, and path and bindings if thye are different. if it doesnt exist, add it to the sites list
                var existingSite = sites.FirstOrDefault(a => a.WebSiteId == site.WebSiteId);

                if (existingSite != null)
                {
                    existingSite.Name = site.Name;
                    existingSite.PhysicalPath = site.PhysicalPath;
                    foreach (var binding in site.SiteBindings)
                    {
                        var existingBinding = existingSite.SiteBindings.FirstOrDefault(a => a.HostName == binding.HostName && a.Protocol == binding.Protocol && a.Port == binding.Port);
                        if (existingBinding == null)
                        {
                            Log.Info($"Adding {site.Name} new binding {binding.HostName} in the database..");
                            existingSite.SiteBindings.Add(binding);
                        }
                    }

                    // remove bindings no longer present
                    foreach (var binding in existingSite.SiteBindings.ToArray())
                    {
                        var existingBinding = site.SiteBindings.FirstOrDefault(a => a.HostName == binding.HostName && a.Protocol == binding.Protocol && a.Port == binding.Port);
                        if (existingBinding == null)
                        {
                            Log.Info($"Removing {site.Name} binding {binding.HostName}:{binding.Port} ({binding.Protocol}) from the database..");
                            existingSite.SiteBindings.Remove(binding);
                        }
                    }
                }
                else
                {
                    Log.Info($"Adding {site.Name} new site in the database..");
                    sites.Add(site);
                }
            }

            foreach (var site in sites.ToArray())
            {
                var existingSite = newList.FirstOrDefault(a => a.WebSiteId == site.WebSiteId);
                if (existingSite == null)
                {
                    Log.Info($"Removing {site.Name} site from the database..");
                    sites.Remove(site);
                }
            }

            await SaveDatabaseAsync();
        }

        /// <summary>
        /// Validate if a short name is valid (letters, numbers only, no spaces or special characters)
        /// </summary>
        /// <param name="shortName">Short name to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidShortName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return false;
            }

            // Alphanumeric check
            return Regex.IsMatch(shortName, "^[a-zA-Z0-9]+$");
        }

        /// <summary>
        /// Creates a new website in IIS with the specified parameters
        /// </summary>
        /// <param name="displayName">Display name for the website</param>
        /// <param name="shortName">Short alphanumeric name for the website</param>
        /// <param name="path">Optional path to create the website in</param>
        /// <returns>The created website object</returns>
        public static async Task<WebSite> CreateWebSiteAsync(string displayName, string shortName, string? path = null)
        {
            ServerManager sm;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name cannot be empty", nameof(displayName));
            }

            if (!IsValidShortName(shortName))
            {
                throw new ArgumentException("Short name must contain only letters and numbers, with no spaces or special characters", nameof(shortName));
            }

            sm = new ServerManager();
            string physicalPath = path == null ? Path.Combine(Global.SitesFolder, shortName) : path;
            string hostName = $"{shortName}.{Global.ServerFqdn}";

            // Create the website directory if it doesn't exist
            if (!Directory.Exists(physicalPath))
            {
                Log.InfoFormat("Creating website directory: {0}", physicalPath);
                Directory.CreateDirectory(physicalPath);
            }

            // Check if site already exists
            var existingSite = sm.Sites.FirstOrDefault(s => s.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase));
            if (existingSite != null)
            {
                throw new ApplicationException($"A website with the name '{displayName}' already exists in IIS");
            }

            // Create the site
            var site = sm.Sites.Add(displayName, physicalPath, 80);

            // Create or get application pool
            try
            {
                var pool = sm.ApplicationPools.FirstOrDefault(p => p.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase));

                if (pool == null)
                {
                    Log.InfoFormat("Creating application pool: {0}", shortName);
                    pool = sm.ApplicationPools.Add(shortName);

                    // Configure app pool settings
                    pool.ManagedRuntimeVersion = "v4.0";
                    pool.ManagedPipelineMode = ManagedPipelineMode.Integrated;
                    pool.AutoStart = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error creating application pool: {ex.Message}", ex);
                throw;
            }

            // Set application pool for site
            site.ApplicationDefaults.ApplicationPoolName = shortName;

            // Clear any existing bindings and add our binding
            site.Bindings.Clear();
            site.Bindings.Add($"*:80:{hostName}", "http");

            // Commit all changes
            try
            {
                sm.CommitChanges();
                Log.InfoFormat("Website '{0}' successfully created with binding {1}", displayName, hostName);
            }
            catch (Exception ex)
            {
                Log.Error($"Error committing website changes: {ex.Message}", ex);
                throw;
            }

            // Create a default document in the site directory
            try
            {
                string defaultDocPath = Path.Combine(physicalPath, "index.html");
                if (!File.Exists(defaultDocPath))
                {
                    string defaultContent = $"<!DOCTYPE html>\n<html>\n<head>\n  <title>{displayName}</title>\n</head>\n<body>\n  <h1>Welcome to {displayName}</h1>\n  <p>This site was created at {DateTime.Now}</p>\n</body>\n</html>";
                    File.WriteAllText(defaultDocPath, defaultContent);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not create default document: {ex.Message}", ex);
            }

            // Return the website object
            WebSite webSite = new WebSite
            {
                Name = displayName,
                ShortName = shortName,
                PhysicalPath = physicalPath,
                WebSiteId = site.Id,
                SiteBindings = new List<WebSiteBinding>
                {
                    new WebSiteBinding
                    {
                        HostName = hostName,
                        Port = 80,
                        Protocol = "http"
                    }
                }
            };

            sites.Add(webSite);

            await SaveDatabaseAsync();

            return webSite;
        }

        /// <summary>
        /// Gets all websites from IIS
        /// </summary>
        /// <returns>Array of all websites</returns>
        public static List<WebSite> GetWebsites()
        {
            return sites;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static WebSite? GetWebSite(long id)
        {
            return sites.FirstOrDefault(a => a.WebSiteId == id);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shortName"></param>
        /// <returns></returns>
        public static WebSite? GetWebSite(string shortName)
        {
            return sites.FirstOrDefault(a => a.ShortName == shortName);
        }

        /// <summary>
        /// Gets a website by its binding hostname
        /// </summary>
        /// <param name="hostbinding">Hostname binding to search for</param>
        /// <returns>Website with matching binding, or null if not found</returns>
        public static WebSite? GetWebsiteByBinding(string hostbinding)
        {
            foreach (WebSite site in sites)
            {
                if (site.SiteBindings.FirstOrDefault(a => a.HostName == hostbinding.ToLower()) != null)
                {
                    return site;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a website by its binding hostname using the server manager
        /// </summary>
        /// <param name="sm">Server manager</param>
        /// <param name="hostbinding">Hostname binding to search for</param>
        /// <returns>Website with matching binding, or null if not found</returns>
        public static Site? GetWebsiteByBinding(ServerManager sm, string hostbinding)
        {
            List<WebSite> sites = new List<WebSite>();

            foreach (var item in sm.Sites)
            {
                foreach (Binding binding in item.Bindings)
                {
                    if (binding.Host.ToLower() == hostbinding.ToLower())
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="site"></param>
        /// <param name="hostname"></param>
        /// <param name="protocol"></param>
        /// <param name="port"></param>
        public async static ValueTask RemoveWebSiteBindingAsync(WebSite site, string hostname, string protocol, int port)
        {
            ServerManager sm = new ServerManager();

            Log.Info($"Attempting to remove binding {hostname}:{port} from site {site.Name}");

            var iisSite = sm.Sites.FirstOrDefault(a => a.Id == site.WebSiteId);

            if (iisSite != null)
            {
                Log.Info($"Found site {iisSite.Name} with ID {iisSite.Id}");

                var binding = iisSite.Bindings.FirstOrDefault(a => a.Host.Equals(hostname, StringComparison.OrdinalIgnoreCase) && a.Protocol == protocol && a.EndPoint.Port == port);

                if (binding != null)
                {
                    Log.Info($"Found binding {hostname}:{port} on site {iisSite.Name}");

                    binding["sslFlags"] = 1;
                    sm.CommitChanges();
                    sm.Dispose();

                    sm = new ServerManager();
                    iisSite = sm.Sites.FirstOrDefault(a => a.Id == site.WebSiteId);
                    binding = iisSite!.Bindings.FirstOrDefault(a => a.Host.Equals(hostname, StringComparison.OrdinalIgnoreCase) && a.Protocol == protocol && a.EndPoint.Port == port);

                    iisSite.Bindings.Remove(binding);
                    sm.CommitChanges();

                    Log.Info($"Removed binding {hostname}:{port} from site {site.Name}");
                }

                // remove the binding from the cached database
                var wsb = site.SiteBindings.FirstOrDefault(a => a.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase) && a.Port == port && a.Protocol == protocol);
                if (wsb != null)
                {
                    site.SiteBindings.Remove(wsb);
                }
            }

            await SaveDatabaseAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static async ValueTask SaveDatabaseAsync()
        {
            string buffer;

            lock (sites)
            {
                buffer = JsonConvert.SerializeObject(sites, Formatting.Indented);
            }

            await File.WriteAllTextAsync(@".\Configuration\WebSites.json", buffer);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="site"></param>
        /// <param name="displayName"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static async ValueTask UpdateWebSiteAsync(WebSite site, string displayName, string path)
        {
            ServerManager sm = new ServerManager();

            var iisSite = sm.Sites.FirstOrDefault(a => a.Id == site.WebSiteId);

            if (iisSite == null)
            {
                throw new FileNotFoundException($"Could not find site with ID {site.WebSiteId}");
            }

            Log.Info($"Updating site {site.Name} with new display name {displayName} and path {path}");

            iisSite.Name = displayName;
            iisSite.Applications["/"].VirtualDirectories["/"].PhysicalPath = path;

            sm.CommitChanges();

            await SaveDatabaseAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="displayName"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static bool WebsiteDisplayNameExists(string displayName, long? id = null)
        {
            ServerManager sm = new ServerManager();

            foreach (var site in sm.Sites)
            {
                if (site.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase))
                {
                    if (id != null && site.Id != id)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a website with the given shortname already exists
        /// </summary>
        /// <param name="shortName">Shortname to check</param>
        /// <returns>True if exists, false otherwise</returns>
        public static bool WebsiteShortNameExists(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return false;
            }

            ServerManager sm = new ServerManager();
            string hostName = $"{shortName}.{Global.ServerFqdn}";

            foreach (var site in sm.Sites)
            {
                foreach (var binding in site.Bindings)
                {
                    if (binding.Host.Equals(hostName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Also check if app pool exists with this name
                if (sm.ApplicationPools.Any(p => p.Name.Equals(shortName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}