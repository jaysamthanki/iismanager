namespace Techie.IISManager.Structures
{
    using log4net;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using TypeCodes;

    /// <summary>
    /// Represents a certificate request with all necessary details and operations.
    /// </summary>
    public class CertificateRequest
    {
        /// <summary>
        /// Logger instance for logging information and errors.
        /// </summary>
        public static ILog Log { get; } = LogManager.GetLogger(typeof(CertificateRequest));

        /// <summary>
        /// Internal database of certificate requests.
        /// </summary>
        protected static List<CertificateRequest> requests;

        /// <summary>
        /// Gets or sets the unique identifier for the certificate request.
        /// </summary>
        public Guid CertificateRequestId { get; set; }

        /// <summary>
        /// Gets or sets the external certificate identifier.
        /// </summary>
        public string ExternalCertificateId { get; set; }

        /// <summary>
        /// Gets or sets the certificate signing request (CSR).
        /// </summary>
        public string Csr { get; set; }

        /// <summary>
        /// Gets or sets the private key associated with the certificate request.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the intermediate certificate.
        /// </summary>
        public string IntermediateCertificate { get; set; }

        /// <summary>
        /// Gets or sets the certificate.
        /// </summary>
        public string Certificate { get; set; }

        /// <summary>
        /// Gets or sets the city associated with the certificate request.
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// Gets or sets the state associated with the certificate request.
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// Gets or sets the country associated with the certificate request.
        /// </summary>
        public string Country { get; set; }

        /// <summary>
        /// Gets or sets the organizational unit associated with the certificate request.
        /// </summary>
        public string OrganizationalUnit { get; set; }

        /// <summary>
        /// Gets or sets the common name (domain) for the certificate request.
        /// </summary>
        public string CommonName { get; set; }

        /// <summary>
        /// Gets or sets the key length for the certificate request.
        /// </summary>
        public int KeyLength { get; set; }

        /// <summary>
        /// Gets or sets the expiration date of the certificate.
        /// </summary>
        public DateTime ExpirationDate { get; set; }

        /// <summary>
        /// Gets or sets the organization name associated with the certificate request.
        /// </summary>
        public string OrganizationName { get; set; }

        /// <summary>
        /// Gets or sets the first name of the administrator.
        /// </summary>
        public string AdminFirstName { get; set; }

        /// <summary>
        /// Gets or sets the last name of the administrator.
        /// </summary>
        public string AdminLastName { get; set; }

        /// <summary>
        /// Gets or sets the first line of the address.
        /// </summary>
        public string AddressLine1 { get; set; }

        /// <summary>
        /// Gets or sets the second line of the address.
        /// </summary>
        public string AddressLine2 { get; set; }

        /// <summary>
        /// Gets or sets the postal code associated with the certificate request.
        /// </summary>
        public string PostalCode { get; set; }

        /// <summary>
        /// Gets or sets the phone number associated with the certificate request.
        /// </summary>
        public string PhoneNumber { get; set; }

        /// <summary>
        /// Gets or sets the email address for the certificate.
        /// </summary>
        public string EmailAddressForCert { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a www binding can be added.
        /// </summary>
        public bool CanAddWwwBinding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether non-SSL bindings can be removed.
        /// </summary>
        public bool CanRemoveNonSSLBinding { get; set; }

        /// <summary>
        /// Gets or sets the date the certificate request was created.
        /// </summary>
        public DateTime DateCreated { get; set; }

        /// <summary>
        /// Gets or sets the certificate authority provider.
        /// </summary>
        public CertificateAuthorityProvider CertificateAuthorityProvider { get; set; }

        /// <summary>
        /// Gets or sets the status of the certificate request.
        /// </summary>
        public CertificateRequestStatus CertificateRequestStatus { get; set; }

        /// <summary>
        /// Gets or sets the subject alternative names for the certificate request.
        /// </summary>
        public List<string> SubjectAlternativeNames { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the certificate can be auto-renewed.
        /// </summary>
        public bool CanAutoRenew { get; set; }

        /// <summary>
        /// Gets or sets the date the certificate request was last checked.
        /// </summary>
        public DateTime LastCheckDate { get; set; }

        /// <summary>
        /// Gets or sets the activation date of the certificate.
        /// </summary>
        public DateTime ActivationDate { get; set; }

        /// <summary>
        /// Static constructor to initialize the internal database and load existing requests.
        /// </summary>
        static CertificateRequest()
        {
            // Prepare the database
            requests = new List<CertificateRequest>();

            if (File.Exists(Path.Combine(Global.DataFolder, "CertificateRequests.xml")))
            {
                Log.Warn("Converting legacy XML database to JSON");
                requests.AddRange((CertificateRequest[])Global.DeserializeObject(Path.Combine(Global.DataFolder, "CertificateRequests.xml"), typeof(CertificateRequest[]))!);
                SaveDatabase();

                try
                {
                    File.Delete(Path.Combine(Global.DataFolder, "CertificateRequests.xml"));
                }
                catch (Exception ex)
                {
                    Log.Error("Unable to delete legacy XML database", ex);
                }
            }

            if (File.Exists(Path.Combine(Global.DataFolder, "CertificateRequests.json")))
            {
                string buffer = File.ReadAllText(Path.Combine(Global.DataFolder, "CertificateRequests.json"));
                requests = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CertificateRequest>>(buffer)!;
            }

            foreach (CertificateRequest cr in requests.Where(a => a.ExpirationDate < DateTime.Now))
            {
                Log.Warn($"Certificate {cr.CertificateRequestId} for domain {cr.CommonName} has expired!");
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateRequest"/> class with default values.
        /// </summary>
        public CertificateRequest()
        {
            this.CertificateRequestId = Guid.NewGuid();
            this.Csr = string.Empty;
            this.Key = string.Empty;
            this.City = string.Empty;
            this.State = string.Empty;
            this.Country = string.Empty;
            this.OrganizationalUnit = string.Empty;
            this.CommonName = string.Empty;
            this.KeyLength = 4096;
            this.Certificate = string.Empty;
            this.ExpirationDate = DateTime.MaxValue;
            this.CertificateRequestStatus = CertificateRequestStatus.New;
            this.ExternalCertificateId = string.Empty;
            this.IntermediateCertificate = string.Empty;
            this.OrganizationName = string.Empty;
            this.AddressLine1 = string.Empty;
            this.AddressLine2 = string.Empty;
            this.AdminFirstName = string.Empty;
            this.AdminLastName = string.Empty;
            this.EmailAddressForCert = string.Empty;
            this.PhoneNumber = string.Empty;
            this.EmailAddressForCert = string.Empty;
            this.CanAddWwwBinding = false;
            this.CanRemoveNonSSLBinding = false;
            this.DateCreated = DateTime.Now;
            this.CertificateAuthorityProvider = CertificateAuthorityProvider.NameCheap;
            this.SubjectAlternativeNames = [];
            this.CanAutoRenew = true;
            this.ActivationDate = DateTime.MaxValue;
            this.LastCheckDate = DateTime.MaxValue;
            this.PostalCode = string.Empty;
            this.SubjectAlternativeNames = [];
        }

        /// <summary>
        /// Adds a new certificate request asynchronously.
        /// </summary>
        /// <param name="csr">The certificate request to add.</param>
        public async static ValueTask AddAsync(CertificateRequest csr)
        {
            Log.Info($"Adding certificate request {csr.CertificateRequestId} for host {csr.CommonName}");

            lock (requests)
            {
                if (requests.FirstOrDefault(a => a.CertificateRequestId == csr.CertificateRequestId) == null)
                {
                    requests.Add(csr);
                }
            }

            await SaveDatabaseAsync();
        }

        /// <summary>
        /// Deletes an existing certificate request asynchronously.
        /// </summary>
        /// <param name="cr">The certificate request to delete.</param>
        public async static ValueTask DeleteRequestAsync(CertificateRequest cr)
        {
            Log.Info($"Deleting certificate request {cr.CertificateRequestId} for host {cr.SubjectAlternativeNames[0]}");

            lock (requests)
            {
                var request = requests.FirstOrDefault(a => a.CertificateRequestId == cr.CertificateRequestId);
                if (request != null)
                {
                    requests.Remove(request);
                }
            }

            await SaveDatabaseAsync();
        }

        /// <summary>
        /// Fixes the subject alternative names by ensuring the common name and www binding are included.
        /// </summary>
        public void FixSubjectAlternativeNames()
        {
            List<string> sans = new List<string>();

            if (this.SubjectAlternativeNames != null)
            {
                foreach (string item in this.SubjectAlternativeNames)
                {
                    sans.Add(item.ToLower().Trim());
                }
            }

            if (!sans.Contains(this.CommonName.ToLower().Trim()))
            {
                sans.Add(this.CommonName.ToLower().Trim());
            }

            if (this.CanAddWwwBinding)
            {
                if (!sans.Contains("www." + this.CommonName.ToLower().Trim()))
                {
                    sans.Add("www." + this.CommonName.ToLower().Trim());
                }

                this.CanAddWwwBinding = false;
            }

            this.SubjectAlternativeNames = sans;
        }

        /// <summary>
        /// Gets all certificate requests.
        /// </summary>
        /// <returns>An array of all certificate requests.</returns>
        public static CertificateRequest[] GetAllRequests()
        {
            return requests.ToArray();
        }

        /// <summary>
        /// Gets the total count of certificate requests.
        /// </summary>
        /// <returns>The total count of certificate requests.</returns>
        public static int GetCertificateCount()
        {
            return requests.Count;
        }

        /// <summary>
        /// Gets the count of certificate requests with a specific status.
        /// </summary>
        /// <param name="status">The status to filter by.</param>
        /// <returns>The count of certificate requests with the specified status.</returns>
        public static int GetCertificateCount(CertificateRequestStatus status)
        {
            return requests.Where(a => a.CertificateRequestStatus == status).Count();
        }

        /// <summary>
        /// Gets a certificate request by its ID.
        /// </summary>
        /// <param name="certificateRequestId">The ID of the certificate request.</param>
        /// <returns>The certificate request with the specified ID, or null if not found.</returns>
        public static CertificateRequest? GetRequest(Guid certificateRequestId)
        {
            return requests.FirstOrDefault(a => a.CertificateRequestId == certificateRequestId);
        }

        /// <summary>
        /// Gets certificate requests by provider and status.
        /// </summary>
        /// <param name="provider">The certificate authority provider.</param>
        /// <param name="status">The status of the certificate requests.</param>
        /// <returns>An array of certificate requests matching the specified provider and status.</returns>
        public static CertificateRequest[] GetRequests(CertificateAuthorityProvider provider, CertificateRequestStatus status)
        {
            return requests.Where(a => a.CertificateAuthorityProvider == provider && a.CertificateRequestStatus == status).ToArray();
        }

        /// <summary>
        /// Checks if a certificate request exists for a specific common name.
        /// </summary>
        /// <param name="commonName">The common name to check.</param>
        /// <returns>True if a certificate request exists for the common name, otherwise false.</returns>
        public static bool HasRequest(string commonName)
        {
            return requests.Where(a => a.CommonName.ToLower() == commonName.ToLower()).Count() > 0;
        }

        /// <summary>
        /// Checks if a certificate request exists for a specific provider and common name.
        /// </summary>
        /// <param name="provider">The certificate authority provider.</param>
        /// <param name="commonName">The common name to check.</param>
        /// <returns>True if a certificate request exists for the provider and common name, otherwise false.</returns>
        public static bool HasRequest(CertificateAuthorityProvider provider, string commonName)
        {
            return requests.Where(a => a.CommonName.ToLower() == commonName.ToLower() && a.CertificateAuthorityProvider == provider).Count() > 0;
        }

        /// <summary>
        /// Saves the certificate database to a JSON file.
        /// </summary>
        public static void SaveDatabase()
        {
            string buffer;
            Log.Info("Saving Certificate Database");

            lock (requests)
            {
                buffer = Newtonsoft.Json.JsonConvert.SerializeObject(requests);
            }

            File.WriteAllText(Path.Combine(Global.DataFolder, "CertificateRequests.json"), buffer);
        }

        /// <summary>
        /// Saves the certificate database to a JSON file asynchronously.
        /// </summary>
        public async static ValueTask SaveDatabaseAsync()
        {
            string buffer;
            Log.Info("Saving Certificate Database");

            lock (requests)
            {
                buffer = Newtonsoft.Json.JsonConvert.SerializeObject(requests);
            }

            await File.WriteAllTextAsync(Path.Combine(Global.DataFolder, "CertificateRequests.json"), buffer);
        }

        /// <summary>
        /// Gets the local IP address of the machine.
        /// </summary>
        /// <returns>The local IP address.</returns>
        protected static string GetLocalIP()
        {
            string localIP = string.Empty;

            var host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }

            return localIP;
        }
    }
}