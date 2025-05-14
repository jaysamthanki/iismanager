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

                    site = null;

                    Log.Info($"Attempting to renew certificate {cr.CertificateRequestId} for domain {cr.CommonName}");

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
                        Log.Error($"Site {cr.SubjectAlternativeNames[0]} not found in IIS, skipping");
                        cr.CertificateRequestStatus = CertificateRequestStatus.Expired;
                        await CertificateRequest.SaveDatabaseAsync();
                        continue;
                    }

                    Log.Info($"Renewing certificate");

                    try
                    {
                        var result = await CreateCertificate(cr, site);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                    }
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
    }
}