namespace Techie.IISManager.Structures
{
    /// <summary>
    /// 
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// 
        /// </summary>
        public string LetsEncryptCrossSignedCert { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ServerPrivateKey { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ServerPrivateKeyThumbprint { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string SiteBasePath { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public Configuration()
        {
            this.LetsEncryptCrossSignedCert = string.Empty;
            this.ServerPrivateKey = string.Empty;
            this.ServerPrivateKeyThumbprint = string.Empty;
            this.SiteBasePath = string.Empty;
        }
    }
}