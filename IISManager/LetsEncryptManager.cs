namespace Techie.IISManager
{
    using Certes;
    using Certes.Acme;
    using Certes.Acme.Resource;
    using log4net;
    using Newtonsoft.Json;
    using Structures;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Timers;
    using TypeCodes;

    /// <summary>
    /// 
    /// </summary>
    public class LetsEncryptManager
    {
        /// <summary>
        /// 
        /// </summary>
        public static bool IsRunning { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public static ILog Log { get; set; } = LogManager.GetLogger(typeof(LetsEncryptManager));

        /// <summary>
        /// 
        /// </summary>
        private static bool IsStagingMode { get; set; }

        /// <summary>
        /// 
        /// </summary>
        protected static Timer timer;

        /// <summary>
        /// 
        /// </summary>
        static LetsEncryptManager()
        {
            IsStagingMode = ConfigurationManager.AppSetting["LetsEncrypt:Staging"] == "true";

            timer = new Timer();
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = false;
            timer.Enabled = false;
            timer.Interval = 1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cr"></param>
        /// <param name="site"></param>
        /// <returns></returns>
        /// <exception cref="ApplicationException"></exception>
        public static async Task<CertificateRequest> CreateCertificate(CertificateRequest cr, WebSite site)
        {
            AcmeContext context;
            IOrderContext order;
            IEnumerable<IAuthorizationContext> authz;
            IChallengeContext challenge;
            IKey certificate;
            CertificateChain certificateChain;
            byte[] pfx;
            bool isComplete = false;
            int countDown;

            cr.CertificateRequestStatus = CertificateRequestStatus.New;

            Log.DebugFormat("Attempting to create a certificate for domains {0}", JsonConvert.SerializeObject(cr.SubjectAlternativeNames, Formatting.Indented));
            Log.DebugFormat("Site base path is {0}", site.PhysicalPath);

            // Failsafe: Ensure port 80 bindings exist for all domains that need Let's Encrypt validation
            await EnsurePort80BindingsExistAsync(cr.SubjectAlternativeNames);

            context = await GetAcmeContextAsync();

            order = await context.NewOrder(cr.SubjectAlternativeNames);
            Log.DebugFormat("Order Response\n{0}", JsonConvert.SerializeObject(order, Formatting.Indented));

            authz = await order.Authorizations();
            Log.DebugFormat("Authorizations Response\n{0}", JsonConvert.SerializeObject(authz, Formatting.Indented));

            foreach (IAuthorizationContext auth in authz)
            {
                challenge = await auth.Http();
                Log.DebugFormat("Http Response\n{0}", JsonConvert.SerializeObject(challenge, Formatting.Indented));

                System.IO.Directory.CreateDirectory(Path.Combine(site.PhysicalPath, ".well-known", "acme-challenge"));
                Log.Debug($"Creating Challenge file {Path.Combine(site.PhysicalPath, ".well-known", "acme-challenge", challenge.Token)}");

                File.WriteAllText(Path.Combine(site.PhysicalPath, ".well-known", "acme-challenge", challenge.Token), challenge.KeyAuthz);

                if (!File.Exists(Path.Combine(site.PhysicalPath, ".well-known", "acme-challenge", "web.config")))
                {
                    Log.Debug("Creating a web.config so the file can be read");
                    File.WriteAllText(Path.Combine(site.PhysicalPath, ".well-known", "acme-challenge", "web.config"), "<?xml version=\"1.0\" encoding=\"UTF-8\"?><configuration> <system.webServer> <staticContent> <mimeMap fileExtension=\".\" mimeType=\"text/json\" /> </staticContent> <handlers> <clear /> <add name=\"StaticFile\" path=\"*\" verb=\"*\" type=\"\" modules=\"StaticFileModule,DefaultDocumentModule,DirectoryListingModule\" scriptProcessor=\"\" resourceType=\"Either\" requireAccess=\"Read\" allowPathInfo=\"false\" preCondition=\"\" responseBufferLimit=\"4194304\" /></handlers></system.webServer></configuration>");
                }

                var response = await challenge.Validate();

                Log.DebugFormat("Validate Response\n{0}", JsonConvert.SerializeObject(response, Formatting.Indented));

                System.Threading.Thread.Sleep(3000);

                var test = await context.HttpClient.Get<Challenge>(response.Url);

                countDown = 6;

                while (!isComplete && countDown > 0)
                {
                    Log.DebugFormat("Got Challenge Response\n{0}", JsonConvert.SerializeObject(test, Formatting.Indented));

                    if (test.Resource.Status != null)
                    {
                        switch (test.Resource.Status.Value)
                        {
                            case ChallengeStatus.Invalid:
                                throw new ApplicationException("Unable to validate domain. " + test.Resource.Error.Detail);

                            case ChallengeStatus.Valid:
                                isComplete = true;
                                break;

                            case ChallengeStatus.Processing:
                            case ChallengeStatus.Pending:
                                Log.Debug("Waiting 5 more seconds...");
                                System.Threading.Thread.Sleep(5000);
                                test = await context.HttpClient.Get<Challenge>(response.Url);
                                break;
                        }
                    }

                    countDown--;
                }

                if (countDown == 0)
                {
                    Log.Error("Unable to authenticate the domain");
                    throw new ApplicationException("Unable to validate domain " + test.Resource.Error.Detail);
                }
            }

            certificate = KeyFactory.NewKey(KeyAlgorithm.RS256);

            cr.Key = certificate.ToPem();

            Log.Debug("Generating Certificate");

            certificateChain = await order.Generate(
                new CsrInfo
                {
                    CountryName = cr.Country,
                    State = cr.State,
                    Locality = cr.City,
                    Organization = cr.OrganizationName,
                    OrganizationUnit = cr.OrganizationalUnit,
                    CommonName = cr.CommonName
                }, certificate);

            Log.DebugFormat("Generate Response\n{0}", JsonConvert.SerializeObject(certificateChain, Formatting.Indented));

            Log.Debug("Generating PFX");

            pfx = certificateChain.ToPfx(certificate).Build(cr.CommonName, string.Empty);

            cr.CertificateRequestStatus = CertificateRequestStatus.Issued;

            cr.ExpirationDate = DateTime.Now.AddDays(60);

            Log.Info($"Setting expiration date to {cr.ExpirationDate}");

            cr.KeyLength = 2048;

            cr.Certificate = certificateChain.ToPem();
            
            await CertificateRequest.AddAsync(cr);

            await SiteManager.BindCertificateAsync(cr, pfx);

            await CertificateRequest.SaveDatabaseAsync();

            return cr;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async static ValueTask<AcmeContext> GetAcmeContextAsync()
        {
            AcmeContext context;
            IAccountContext account;
            Uri letsEncryptUri;
            string accountFileName;

            if (IsStagingMode)
            {
                letsEncryptUri = WellKnownServers.LetsEncryptStagingV2;
                accountFileName = "LetsEncryptAccount.Staging.pem";
            }
            else
            {
                letsEncryptUri = WellKnownServers.LetsEncryptV2;
                accountFileName = "LetsEncryptAccount.pem";
            }

            accountFileName = Path.Combine(Global.DataFolder, accountFileName);

            if (File.Exists(accountFileName))
            {
                context = new AcmeContext(letsEncryptUri, KeyFactory.FromPem(File.ReadAllText(accountFileName)));
                account = await context.Account();
            }
            else
            {
                context = new AcmeContext(letsEncryptUri);
                account = await context.NewAccount(ConfigurationManager.AppSetting["LetsEncrypt:AdministratorEmail"], true);
                await File.WriteAllTextAsync(accountFileName, context.AccountKey.ToPem());
            }

            return context;
        }

        /// <summary>
        /// 
        /// </summary>
        public static void StartService()
        {
            timer = new Timer();
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Enabled = true;
            timer.Interval = 5000;

            IsRunning = true;
            Log.Info("Lets Encrypt Manager Started");
        }

        /// <summary>
        /// 
        /// </summary>
        public static void StopService()
        {
            Log.Info("Lets Encrypt Manager Stopping");
            timer.Enabled = false;
            timer.Stop();
            IsRunning = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async static void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            WebSite? site = null;
            List<CertificateRequest> certificates = [];
            
            // Track renewal results for reporting
            List<string> renewedDomains = [];
            List<string> renewedWithPort80Fix = [];
            List<(string domain, string error)> failedRenewals = [];
            DateTime startTime = DateTime.Now;

            try
            {
                Log.Info("LetsEncrypt Manager checking certs");
                timer.Enabled = false;
                certificates.AddRange(CertificateRequest.GetRequests(CertificateAuthorityProvider.LetsEncrypt, CertificateRequestStatus.Completed));
                certificates.AddRange(CertificateRequest.GetRequests(CertificateAuthorityProvider.LetsEncrypt, CertificateRequestStatus.Expired));
                certificates.AddRange(CertificateRequest.GetRequests(CertificateAuthorityProvider.LetsEncrypt, CertificateRequestStatus.New));

                Log.Debug($"Certificates to process: {certificates.Count}");
                foreach (CertificateRequest cr in certificates)
                {
                    if (cr.ExpirationDate > DateTime.Now)
                    {
                        continue;
                    }

                    // Check if we've exceeded maximum retry attempts
                    if (cr.RenewalAttempts >= cr.MaxRenewalAttempts)
                    {
                        Log.Error($"Certificate {cr.CertificateRequestId} for domain {cr.CommonName} has exceeded maximum renewal attempts ({cr.MaxRenewalAttempts}). Marking as expired.");
                        cr.CertificateRequestStatus = CertificateRequestStatus.Expired;
                        failedRenewals.Add((cr.CommonName, $"Exceeded maximum renewal attempts ({cr.MaxRenewalAttempts}). Last attempt: {cr.LastRenewalAttempt:yyyy-MM-dd HH:mm:ss}"));
                        await CertificateRequest.SaveDatabaseAsync();
                        continue;
                    }

                    // Skip if we attempted renewal recently (within 6 hours) to avoid hitting rate limits
                    if (cr.LastRenewalAttempt > DateTime.MinValue && cr.LastRenewalAttempt.AddHours(6) > DateTime.Now)
                    {
                        Log.Debug($"Skipping certificate {cr.CertificateRequestId} for domain {cr.CommonName} - last attempt was too recent ({cr.LastRenewalAttempt:yyyy-MM-dd HH:mm:ss})");
                        continue;
                    }

                    site = null;

                    Log.Info($"Attempting to renew certificate {cr.CertificateRequestId} for domain {cr.CommonName} (attempt {cr.RenewalAttempts + 1}/{cr.MaxRenewalAttempts})");

                    if (cr.SubjectAlternativeNames == null)
                    {
                        cr.SubjectAlternativeNames = [cr.CommonName];
                    }

                    foreach (string domain in cr.SubjectAlternativeNames)
                    {
                        site = SiteManager.GetWebsiteByBinding(cr.SubjectAlternativeNames[0]);

                        if (site != null)
                        {
                            break;
                        }
                    }

                    if (site == null)
                    {
                        string errorMessage = $"Site {cr.SubjectAlternativeNames[0]} not found in IIS";
                        Log.Error(errorMessage);
                        
                        // Increment retry counter and update last attempt
                        cr.RenewalAttempts++;
                        cr.LastRenewalAttempt = DateTime.Now;
                        
                        if (cr.RenewalAttempts >= cr.MaxRenewalAttempts)
                        {
                            cr.CertificateRequestStatus = CertificateRequestStatus.Expired;
                            errorMessage = $"Site not found after {cr.MaxRenewalAttempts} attempts";
                        }
                        
                        failedRenewals.Add((cr.CommonName, errorMessage));
                        await CertificateRequest.SaveDatabaseAsync();
                        continue;
                    }

                    Log.Info($"Renewing certificate");

                    // Update attempt tracking before trying
                    cr.RenewalAttempts++;
                    cr.LastRenewalAttempt = DateTime.Now;

                    try
                    {
                        // Track if port 80 bindings were added during this renewal
                        bool hadToAddPort80Bindings = await CheckAndReportPort80Bindings(cr.SubjectAlternativeNames, site);
                        
                        var result = await CreateCertificate(cr, site);
                        
                        // Reset retry counter on successful renewal
                        cr.RenewalAttempts = 0;
                        cr.LastRenewalAttempt = DateTime.MinValue;
                        
                        // Track successful renewal
                        string domainList = string.Join(", ", cr.SubjectAlternativeNames);
                        if (hadToAddPort80Bindings)
                        {
                            renewedWithPort80Fix.Add(domainList);
                        }
                        else
                        {
                            renewedDomains.Add(domainList);
                        }
                        
                        Log.Info($"Successfully renewed certificate for {domainList}");
                        await CertificateRequest.SaveDatabaseAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                        
                        string errorMessage = ex.Message;
                        if (cr.RenewalAttempts >= cr.MaxRenewalAttempts)
                        {
                            cr.CertificateRequestStatus = CertificateRequestStatus.Expired;
                            errorMessage = $"Failed after {cr.MaxRenewalAttempts} attempts: {ex.Message}";
                        }
                        
                        failedRenewals.Add((cr.CommonName, errorMessage));
                        await CertificateRequest.SaveDatabaseAsync();
                    }
                }
                
                // Send summary report if there was any activity
                if (renewedDomains.Count > 0 || renewedWithPort80Fix.Count > 0 || failedRenewals.Count > 0)
                {
                    await SendRenewalSummaryReport(renewedDomains, renewedWithPort80Fix, failedRenewals, startTime);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                // Once a day
                timer.Interval = 1000 * 86400;
                timer.Start();
            }
        }

        /// <summary>
        /// Checks and reports if port 80 bindings need to be added
        /// </summary>
        /// <param name="domains">List of domains to check</param>
        /// <param name="site">Website to check</param>
        /// <returns>True if any port 80 bindings were added</returns>
        private static async Task<bool> CheckAndReportPort80Bindings(List<string> domains, WebSite site)
        {
            bool anyBindingsAdded = false;
            
            foreach (string domain in domains)
            {
                bool hasPort80Binding = site.SiteBindings.Any(b => 
                    b.HostName.Equals(domain, StringComparison.OrdinalIgnoreCase) && 
                    b.Protocol == "http" && 
                    b.Port == 80);

                if (!hasPort80Binding)
                {
                    anyBindingsAdded = true;
                    Log.Warn($"Port 80 binding will be added for {domain} during renewal");
                }
            }
            
            return anyBindingsAdded;
        }

        /// <summary>
        /// Ensures port 80 bindings exist for all domains that need Let's Encrypt HTTP-01 validation
        /// </summary>
        /// <param name="domains">List of domains that need validation</param>
        /// <returns>True if any bindings were added, false if all bindings already existed</returns>
        private static async Task<bool> EnsurePort80BindingsExistAsync(List<string> domains)
        {
            bool anyBindingsAdded = false;
            
            foreach (string domain in domains)
            {
                Log.Info($"Checking if port 80 binding exists for domain: {domain}");

                // Find the website that should handle this domain
                WebSite? site = SiteManager.GetWebsiteByBinding(domain);
                if (site == null)
                {
                    Log.Error($"Cannot ensure port 80 binding for {domain} - no website found with this binding");
                    continue;
                }

                // Check if port 80 binding already exists for this domain
                bool hasPort80Binding = site.SiteBindings.Any(b => b.HostName.Equals(domain, StringComparison.OrdinalIgnoreCase) && b.Protocol == "http" && b.Port == 80);

                if (!hasPort80Binding)
                {
                    Log.Warn($"Port 80 binding missing for {domain} on site {site.Name}. Adding it now for Let's Encrypt validation.");
                    
                    try
                    {
                        await SiteManager.AddBindingAsync(site, domain, 80, "http");
                        Log.Warn($"Successfully added port 80 binding for {domain}");
                        anyBindingsAdded = true;
                        
                        // Send immediate alert about port 80 binding being added
                        await EmailManager.SendPort80BindingAlertAsync(domain, site.Name, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to add port 80 binding for {domain}: {ex.Message}", ex);
                        throw new ApplicationException($"Could not ensure port 80 binding for {domain} - Let's Encrypt validation will fail");
                    }
                }
            }
            
            return anyBindingsAdded;
        }

        /// <summary>
        /// Sends a summary report of certificate renewal activities
        /// </summary>
        /// <param name="renewedDomains">Domains that were successfully renewed</param>
        /// <param name="renewedWithPort80Fix">Domains that were renewed but needed port 80 binding fixes</param>
        /// <param name="failedRenewals">Domains that failed to renew with error messages</param>
        /// <param name="startTime">When the renewal process started</param>
        private static async Task SendRenewalSummaryReport(
            List<string> renewedDomains, 
            List<string> renewedWithPort80Fix, 
            List<(string domain, string error)> failedRenewals,
            DateTime startTime)
        {
            try
            {
                TimeSpan duration = DateTime.Now - startTime;
                string subject = $"Let's Encrypt Certificate Renewal Report - {DateTime.Now:yyyy-MM-dd}";
                
                // Determine overall status for alert type
                string alertType = "INFO";
                if (failedRenewals.Count > 0)
                {
                    alertType = "ERROR";
                    subject += " - FAILURES DETECTED";
                }
                else if (renewedWithPort80Fix.Count > 0)
                {
                    alertType = "WARNING";
                    subject += " - PORT 80 FIXES APPLIED";
                }
                else if (renewedDomains.Count > 0)
                {
                    alertType = "SUCCESS";
                    subject += " - ALL SUCCESSFUL";
                }

                // Build the report body
                string htmlBody = $@"
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; }}
                        h2 {{ color: #333; border-bottom: 2px solid #667eea; padding-bottom: 5px; }}
                        h3 {{ color: #667eea; }}
                        .summary {{ background: #f0f0f0; padding: 15px; border-radius: 5px; margin: 20px 0; }}
                        .success {{ color: #4CAF50; }}
                        .warning {{ color: #ff9800; }}
                        .error {{ color: #f44336; }}
                        ul {{ margin: 10px 0; }}
                        li {{ margin: 5px 0; }}
                        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; color: #666; font-size: 12px; }}
                    </style>
                </head>
                <body>
                    <h2>Let's Encrypt Certificate Renewal Report</h2>
                    
                    <div class='summary'>
                        <strong>Server:</strong> {Environment.MachineName}<br>
                        <strong>Date:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}<br>
                        <strong>Duration:</strong> {duration.TotalMinutes:F2} minutes<br>
                        <strong>Total Processed:</strong> {renewedDomains.Count + renewedWithPort80Fix.Count + failedRenewals.Count} certificates
                    </div>";

                // Successfully renewed domains
                if (renewedDomains.Count > 0)
                {
                    htmlBody += $@"
                    <h3 class='success'>✓ Successfully Renewed ({renewedDomains.Count})</h3>
                    <ul>";
                    foreach (var domain in renewedDomains)
                    {
                        htmlBody += $"<li>{domain}</li>";
                    }
                    htmlBody += "</ul>";
                }

                // Renewed with port 80 fixes
                if (renewedWithPort80Fix.Count > 0)
                {
                    htmlBody += $@"
                    <h3 class='warning'>⚠ Renewed with Port 80 Binding Fixes ({renewedWithPort80Fix.Count})</h3>
                    <p><em>These domains were successfully renewed, but required automatic addition of missing port 80 bindings:</em></p>
                    <ul>";
                    foreach (var domain in renewedWithPort80Fix)
                    {
                        htmlBody += $"<li>{domain}</li>";
                    }
                    htmlBody += @"</ul>
                    <p><strong>Action Required:</strong> Review why these port 80 bindings were missing. They may have been manually removed or lost due to configuration changes.</p>";
                }

                // Failed renewals
                if (failedRenewals.Count > 0)
                {
                    htmlBody += $@"
                    <h3 class='error'>✗ Failed Renewals ({failedRenewals.Count})</h3>
                    <p><strong>IMMEDIATE ATTENTION REQUIRED:</strong> The following certificates failed to renew:</p>
                    <ul>";
                    foreach (var (domain, error) in failedRenewals)
                    {
                        htmlBody += $"<li><strong>{domain}:</strong> {error}</li>";
                    }
                    htmlBody += @"</ul>
                    <p><em>Note: Certificates that exceed maximum retry attempts are automatically marked as expired to prevent continuous failed attempts.</em></p>";
                }

                // Check for certificates that are approaching max retry attempts
                var retryingCertificates = CertificateRequest.GetAllRequests()
                    .Where(c => c.CertificateAuthorityProvider == CertificateAuthorityProvider.LetsEncrypt && 
                               c.RenewalAttempts > 0 && 
                               c.RenewalAttempts < c.MaxRenewalAttempts &&
                               c.CertificateRequestStatus != CertificateRequestStatus.Expired)
                    .ToList();

                if (retryingCertificates.Count > 0)
                {
                    htmlBody += $@"
                    <h3 class='warning'>⚠ Certificates with Retry Attempts ({retryingCertificates.Count})</h3>
                    <p>The following certificates have failed previous renewal attempts but haven't reached the maximum limit yet:</p>
                    <ul>";
                    foreach (var cert in retryingCertificates)
                    {
                        htmlBody += $"<li><strong>{cert.CommonName}:</strong> {cert.RenewalAttempts}/{cert.MaxRenewalAttempts} attempts (Last: {cert.LastRenewalAttempt:yyyy-MM-dd HH:mm:ss})</li>";
                    }
                    htmlBody += "</ul>";
                }

                htmlBody += $@"
                    <div class='footer'>
                        <p>This is an automated report from IIS Manager on {Environment.MachineName}</p>
                        <p>Next scheduled check: {DateTime.Now.AddDays(1):yyyy-MM-dd HH:mm:ss}</p>
                    </div>
                </body>
                </html>";

                // Send the report
                await EmailManager.SendAlertAsync(subject, htmlBody, true);
                Log.Info($"Certificate renewal summary report sent. Renewed: {renewedDomains.Count}, Fixed: {renewedWithPort80Fix.Count}, Failed: {failedRenewals.Count}");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to send renewal summary report", ex);
            }
        }
    }
}