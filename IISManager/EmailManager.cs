namespace Techie.IISManager
{
    using log4net;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Mail;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages email alerts and notifications for the IIS Manager application
    /// </summary>
    public class EmailManager
    {
        /// <summary>
        /// Logger for the EmailManager class
        /// </summary>
        private static readonly ILog Log = LogManager.GetLogger(typeof(EmailManager));

        /// <summary>
        /// SMTP server hostname or IP address
        /// </summary>
        private static string smtpServer = string.Empty;

        /// <summary>
        /// SMTP server username for authentication
        /// </summary>
        private static string smtpUsername = string.Empty;

        /// <summary>
        /// SMTP server password for authentication
        /// </summary>
        private static string smtpPassword = string.Empty;

        /// <summary>
        /// SMTP server port
        /// </summary>
        private static int smtpPort = 587;

        /// <summary>
        /// Email address to send alerts from
        /// </summary>
        private static string fromAddress = string.Empty;

        /// <summary>
        /// Email address to send alerts to
        /// </summary>
        private static string alertDestinationAddress = string.Empty;

        /// <summary>
        /// Indicates whether email is configured and available
        /// </summary>
        private static bool isConfigured = false;

        /// <summary>
        /// HTML email template loaded from file
        /// </summary>
        private static string htmlTemplate = string.Empty;

        /// <summary>
        /// Static constructor to initialize email configuration
        /// </summary>
        static EmailManager()
        {
            try
            {
                // Load SMTP configuration from appsettings
                smtpServer = ConfigurationManager.AppSetting["Smtp:SmtpServer"] ?? string.Empty;
                smtpUsername = ConfigurationManager.AppSetting["Smtp:SmtpUsername"] ?? string.Empty;
                smtpPassword = ConfigurationManager.AppSetting["Smtp:SmtpPassword"] ?? string.Empty;
                fromAddress = ConfigurationManager.AppSetting["Smtp:FromAddress"] ?? string.Empty;
                alertDestinationAddress = ConfigurationManager.AppSetting["Smtp:AlertDestinationAddress"] ?? string.Empty;

                // Parse SMTP port with fallback to default
                if (!int.TryParse(ConfigurationManager.AppSetting["Smtp:SmtpPort"], out smtpPort))
                {
                    smtpPort = 587; // Default to STARTTLS port
                }

                // Check if email is properly configured
                isConfigured = !string.IsNullOrWhiteSpace(smtpServer) &&
                              !string.IsNullOrWhiteSpace(fromAddress) &&
                              !string.IsNullOrWhiteSpace(alertDestinationAddress);

                // Load HTML template if email is configured
                if (isConfigured)
                {
                    try
                    {
                        string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "EmailAlertTemplate.html");
                        if (File.Exists(templatePath))
                        {
                            htmlTemplate = File.ReadAllText(templatePath);
                            Log.Info($"Email HTML template loaded from {templatePath}");
                        }
                        else
                        {
                            Log.Warn($"Email HTML template not found at {templatePath}. Will use plain text emails.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Failed to load email HTML template", ex);
                    }

                    Log.Info($"Email manager initialized. Server: {smtpServer}:{smtpPort}, From: {fromAddress}, To: {alertDestinationAddress}");
                }
                else
                {
                    Log.Info("Email manager initialized but not configured. Email alerts will be disabled.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize EmailManager", ex);
                isConfigured = false;
            }
        }

        /// <summary>
        /// Gets whether email alerts are configured and available
        /// </summary>
        public static bool IsConfigured => isConfigured;

        /// <summary>
        /// Creates and configures an SMTP client with the loaded settings
        /// </summary>
        /// <returns>Configured SmtpClient instance</returns>
        private static SmtpClient CreateSmtpClient()
        {
            var client = new SmtpClient
            {
                Host = smtpServer,
                Port = smtpPort,
                EnableSsl = smtpPort == 587 || smtpPort == 465, // Enable SSL for common secure ports
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            // Add credentials if username and password are provided
            if (!string.IsNullOrWhiteSpace(smtpUsername) && !string.IsNullOrWhiteSpace(smtpPassword))
            {
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
            }

            return client;
        }

        /// <summary>
        /// Sends an alert email asynchronously
        /// </summary>
        /// <param name="subject">Email subject</param>
        /// <param name="body">Email body</param>
        /// <param name="isHtml">Whether the body contains HTML</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public static async Task<bool> SendAlertAsync(string subject, string body, bool isHtml = false)
        {
            if (!isConfigured)
            {
                Log.Debug("Email alert requested but email is not configured. Skipping.");
                return false;
            }

            try
            {
                using (var client = CreateSmtpClient())
                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(fromAddress);
                    message.To.Add(new MailAddress(alertDestinationAddress));
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = isHtml;

                    await client.SendMailAsync(message);
                    Log.Info($"Alert email sent successfully. Subject: {subject}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send alert email. Subject: {subject}", ex);
                return false;
            }
        }

        /// <summary>
        /// Sends an alert email with additional recipients
        /// </summary>
        /// <param name="subject">Email subject</param>
        /// <param name="body">Email body</param>
        /// <param name="additionalRecipients">Additional email addresses to send to</param>
        /// <param name="isHtml">Whether the body contains HTML</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public static async Task<bool> SendAlertAsync(string subject, string body, string[] additionalRecipients, bool isHtml = false)
        {
            if (!isConfigured)
            {
                Log.Debug("Email alert requested but email is not configured. Skipping.");
                return false;
            }

            try
            {
                using (var client = CreateSmtpClient())
                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(fromAddress);
                    message.To.Add(new MailAddress(alertDestinationAddress));
                    
                    // Add additional recipients
                    if (additionalRecipients != null)
                    {
                        foreach (var recipient in additionalRecipients)
                        {
                            if (!string.IsNullOrWhiteSpace(recipient))
                            {
                                message.To.Add(new MailAddress(recipient));
                            }
                        }
                    }

                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = isHtml;

                    await client.SendMailAsync(message);
                    Log.Info($"Alert email sent successfully to {message.To.Count} recipients. Subject: {subject}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send alert email. Subject: {subject}", ex);
                return false;
            }
        }

        /// <summary>
        /// Tests the email configuration by sending a test message
        /// </summary>
        /// <returns>True if test email was sent successfully, false otherwise</returns>
        public static async Task<bool> TestEmailConfigurationAsync()
        {
            if (!isConfigured)
            {
                Log.Warn("Cannot test email configuration - email is not configured.");
                return false;
            }

            try
            {
                string testSubject = $"IIS Manager Email Test - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                string testBody = $@"This is a test email from IIS Manager.

Configuration Details:
- SMTP Server: {smtpServer}:{smtpPort}
- From Address: {fromAddress}
- Alert Destination: {alertDestinationAddress}
- Server Name: {Environment.MachineName}
- Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

If you received this email, your email configuration is working correctly.";

                return await SendAlertAsync(testSubject, testBody);
            }
            catch (Exception ex)
            {
                Log.Error("Email configuration test failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Sends a certificate renewal alert
        /// </summary>
        /// <param name="domain">Domain name for the certificate</param>
        /// <param name="action">Action taken (e.g., "renewed", "failed")</param>
        /// <param name="details">Additional details about the action</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public static async Task<bool> SendCertificateAlertAsync(string domain, string action, string details)
        {
            if (!isConfigured)
            {
                return false;
            }

            string subject = $"IIS Manager Certificate Alert - {domain} - {action}";
            string body = $@"Certificate Management Alert

Domain: {domain}
Action: {action}
Server: {Environment.MachineName}
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

Details:
{details}

This is an automated message from IIS Manager.";

            return await SendAlertAsync(subject, body);
        }

        /// <summary>
        /// Sends a port 80 binding alert
        /// </summary>
        /// <param name="domain">Domain name</param>
        /// <param name="siteName">Website name</param>
        /// <param name="wasAdded">Whether the binding was added or just checked</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public static async Task<bool> SendPort80BindingAlertAsync(string domain, string siteName, bool wasAdded)
        {
            if (!isConfigured)
            {
                return false;
            }

            string action = wasAdded ? "ADDED" : "VERIFIED";
            string subject = $"IIS Manager Port 80 Binding Alert - {domain} - {action}";
            
            // Use HTML template if available
            if (!string.IsNullOrWhiteSpace(htmlTemplate))
            {
                string htmlBody = GenerateHtmlAlert(
                    alertType: wasAdded ? "WARNING" : "INFO",
                    alertMessage: wasAdded ? 
                        "A missing port 80 binding was detected and automatically added. This binding is required for Let's Encrypt certificate validation." :
                        "Port 80 binding was verified to exist. No action was required.",
                    domain: domain,
                    websiteName: siteName,
                    additionalDetails: new Dictionary<string, string>
                    {
                        { "Action", action },
                        { "Binding Type", "HTTP (Port 80)" }
                    },
                    actionRequired: wasAdded ? @"
                        <div class='action-required'>
                            <h3>Recommended Actions</h3>
                            <p>A port 80 binding was missing and has been automatically added. Please review:</p>
                            <ul>
                                <li>Check if someone manually removed the binding</li>
                                <li>Review IIS configuration changes</li>
                                <li>Ensure firewall allows port 80 traffic</li>
                            </ul>
                        </div>" : null
                );
                
                return await SendAlertAsync(subject, htmlBody, true);
            }

            // Fallback to plain text
            string body = $@"Port 80 Binding Alert

Domain: {domain}
Website: {siteName}
Action: {action}
Server: {Environment.MachineName}
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

{(wasAdded ? 
@"A missing port 80 binding was detected and automatically added.
This binding is required for Let's Encrypt certificate validation.
The binding may have been manually removed or lost due to configuration changes." :
@"Port 80 binding was verified to exist.
No action was required.")}

This is an automated message from IIS Manager.";

            return await SendAlertAsync(subject, body);
        }

        /// <summary>
        /// Generates an HTML alert email using the template
        /// </summary>
        /// <param name="alertType">Type of alert (INFO, WARNING, ERROR, SUCCESS)</param>
        /// <param name="alertMessage">Main alert message</param>
        /// <param name="domain">Domain name</param>
        /// <param name="websiteName">Website name</param>
        /// <param name="additionalDetails">Additional details as key-value pairs</param>
        /// <param name="actionRequired">HTML for action required section</param>
        /// <param name="technicalDetails">HTML for technical details section</param>
        /// <param name="errorSection">HTML for error section</param>
        /// <returns>Generated HTML email body</returns>
        private static string GenerateHtmlAlert(
            string alertType,
            string alertMessage,
            string domain,
            string websiteName,
            Dictionary<string, string> additionalDetails = null,
            string actionRequired = null,
            string technicalDetails = null,
            string errorSection = null)
        {
            if (string.IsNullOrWhiteSpace(htmlTemplate))
            {
                return string.Empty;
            }

            string html = htmlTemplate;
            
            // Set alert type and CSS class
            string alertClass = alertType.ToLower() switch
            {
                "error" => "alert-error",
                "warning" => "alert-warning",
                "success" => "alert-success",
                _ => "alert-info"
            };
            
            string messageClass = alertType.ToLower() switch
            {
                "error" => "error",
                "warning" => "warning",
                "success" => "success",
                _ => ""
            };

            // Replace template variables
            html = html.Replace("{{ALERT_TYPE}}", alertType)
                      .Replace("{{ALERT_CLASS}}", alertClass)
                      .Replace("{{ALERT_MESSAGE}}", alertMessage)
                      .Replace("{{MESSAGE_CLASS}}", messageClass)
                      .Replace("{{SERVER_NAME}}", Environment.MachineName)
                      .Replace("{{DOMAIN}}", domain ?? "N/A")
                      .Replace("{{WEBSITE_NAME}}", websiteName ?? "N/A")
                      .Replace("{{TIMESTAMP}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                      .Replace("{{ADMIN_EMAIL}}", ConfigurationManager.AppSetting["LetsEncrypt:AdministratorEmail"] ?? "admin@example.com");

            // Add additional details
            if (additionalDetails != null && additionalDetails.Count > 0)
            {
                var detailsHtml = string.Empty;
                foreach (var detail in additionalDetails)
                {
                    detailsHtml += $@"
                <div class='detail-row'>
                    <span class='detail-label'>{detail.Key}:</span>
                    <span class='detail-value'>{detail.Value}</span>
                </div>";
                }
                html = html.Replace("{{ADDITIONAL_DETAILS}}", detailsHtml);
            }
            else
            {
                html = html.Replace("{{ADDITIONAL_DETAILS}}", string.Empty);
            }

            // Add optional sections
            html = html.Replace("{{ACTION_REQUIRED_SECTION}}", actionRequired ?? string.Empty)
                      .Replace("{{TECHNICAL_DETAILS_SECTION}}", technicalDetails ?? string.Empty)
                      .Replace("{{ERROR_SECTION}}", errorSection ?? string.Empty);

            return html;
        }

        /// <summary>
        /// Sends an HTML certificate renewal alert
        /// </summary>
        /// <param name="domain">Domain name for the certificate</param>
        /// <param name="action">Action taken (e.g., "Renewed Successfully", "Renewal Failed")</param>
        /// <param name="details">Additional details about the action</param>
        /// <param name="isError">Whether this is an error alert</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public static async Task<bool> SendCertificateHtmlAlertAsync(string domain, string action, string details, bool isError = false)
        {
            if (!isConfigured)
            {
                return false;
            }

            string subject = $"IIS Manager Certificate Alert - {domain} - {action}";
            
            // Use HTML template if available
            if (!string.IsNullOrWhiteSpace(htmlTemplate))
            {
                string htmlBody = GenerateHtmlAlert(
                    alertType: isError ? "ERROR" : "SUCCESS",
                    alertMessage: $"Certificate {action} for {domain}",
                    domain: domain,
                    websiteName: SiteManager.GetWebsiteByBinding(domain)?.Name ?? "Unknown",
                    additionalDetails: new Dictionary<string, string>
                    {
                        { "Certificate Action", action },
                        { "Status", isError ? "Failed" : "Completed" }
                    },
                    technicalDetails: string.IsNullOrWhiteSpace(details) ? null : $@"
                        <div class='section'>
                            <div class='section-title'>Technical Details</div>
                            <div class='code-block'>{details}</div>
                        </div>",
                    errorSection: isError && !string.IsNullOrWhiteSpace(details) ? $@"
                        <div class='section'>
                            <div class='section-title'>Error Information</div>
                            <div class='message-box error'>
                                {details}
                            </div>
                        </div>" : null
                );
                
                return await SendAlertAsync(subject, htmlBody, true);
            }

            // Fallback to existing plain text method
            return await SendCertificateAlertAsync(domain, action, details);
        }
    }
}