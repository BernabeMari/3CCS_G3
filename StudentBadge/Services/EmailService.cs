using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using MimeKit.Text;
using Microsoft.Extensions.Logging;

namespace StudentBadge.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendPinEmailAsync(string toEmail, string toName, string pin)
        {
            try
            {
                _logger.LogInformation("Preparing to send PIN email to {Email} with name {Name}", toEmail, toName);
                
                var email = new MimeMessage();
                
                // Set sender from app settings
                string fromEmail = _configuration["EmailSettings:FromEmail"];
                string fromName = _configuration["EmailSettings:FromName"];
                string gmailPassword = _configuration["EmailSettings:Password"];
                
                _logger.LogInformation("Using sender email: {Email}, sender name: {Name}", fromEmail, fromName);
                
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(gmailPassword))
                {
                    _logger.LogError("Email settings are not configured correctly. FromEmail: {HasEmail}, Password: {HasPassword}",
                        !string.IsNullOrEmpty(fromEmail),
                        !string.IsNullOrEmpty(gmailPassword));
                    return false;
                }
                
                email.From.Add(new MailboxAddress(fromName ?? "Student Badge System", fromEmail));
                email.To.Add(new MailboxAddress(toName, toEmail));
                
                email.Subject = "Your Verification PIN for Student Badge System";
                
                // Simple HTML email template
                string htmlBody = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: #4A6FDC; color: white; padding: 10px 20px; text-align: center; }}
                        .content {{ padding: 20px; }}
                        .pin {{ font-size: 24px; font-weight: bold; text-align: center; padding: 10px; 
                               background-color: #f0f0f0; margin: 15px 0; letter-spacing: 5px; }}
                        .footer {{ font-size: 12px; text-align: center; margin-top: 20px; color: #666; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>Student Badge System</h2>
                        </div>
                        <div class='content'>
                            <p>Hello {toName},</p>
                            <p>Your verification PIN for the Student Badge System is:</p>
                            <div class='pin'>{pin}</div>
                            <p>Use this PIN to verify your account. This PIN is confidential and should not be shared with others.</p>
                            <p>If you did not request this PIN, please ignore this email.</p>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message. Please do not reply to this email.</p>
                        </div>
                    </div>
                </body>
                </html>";
                
                email.Body = new TextPart(TextFormat.Html) { Text = htmlBody };
                
                _logger.LogInformation("Attempting to connect to SMTP server: smtp.gmail.com:587");
                
                // Send email
                using (var smtp = new SmtpClient())
                {
                    // Configure timeout
                    smtp.Timeout = 30000; // 30 seconds
                    
                    try
                    {
                        await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                        _logger.LogInformation("Connected to SMTP server successfully");
                        
                        // Try to authenticate
                        _logger.LogInformation("Attempting to authenticate with Gmail");
                        await smtp.AuthenticateAsync(fromEmail, gmailPassword);
                        _logger.LogInformation("Authentication successful");
                        
                        // Send the email
                        _logger.LogInformation("Sending email to {Email}", toEmail);
                        await smtp.SendAsync(email);
                        _logger.LogInformation("Email sent successfully");
                        
                        await smtp.DisconnectAsync(true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Specific error in email sending process");
                        
                        // Provide helpful information about Gmail security settings
                        if (ex.Message.Contains("Authentication") || ex.Message.Contains("5.7.8") || 
                            ex.Message.Contains("5.7.0") || ex.Message.Contains("5.7.14"))
                        {
                            _logger.LogError("Gmail authentication failed. This is likely due to Gmail security settings.");
                            _logger.LogError("Please ensure that:");
                            _logger.LogError("1. The app password is correct (not your regular Gmail password)");
                            _logger.LogError("2. You have enabled 2-factor authentication for your Gmail account");
                            _logger.LogError("3. You have generated an app-specific password at https://myaccount.google.com/apppasswords");
                        }
                        
                        throw; // Re-throw to be caught by the outer catch block
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending PIN email to {Email}: {ErrorMessage}", toEmail, ex.Message);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string toName, string resetCode)
        {
            try
            {
                _logger.LogInformation("Preparing to send password reset email to {Email} with name {Name}", toEmail, toName);
                
                var email = new MimeMessage();
                
                // Set sender from app settings
                string fromEmail = _configuration["EmailSettings:FromEmail"];
                string fromName = _configuration["EmailSettings:FromName"];
                string gmailPassword = _configuration["EmailSettings:Password"];
                
                _logger.LogInformation("Using sender email: {Email}, sender name: {Name}", fromEmail, fromName);
                
                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(gmailPassword))
                {
                    _logger.LogError("Email settings are not configured correctly. FromEmail: {HasEmail}, Password: {HasPassword}",
                        !string.IsNullOrEmpty(fromEmail),
                        !string.IsNullOrEmpty(gmailPassword));
                    return false;
                }
                
                email.From.Add(new MailboxAddress(fromName ?? "Student Badge System", fromEmail));
                email.To.Add(new MailboxAddress(toName, toEmail));
                
                email.Subject = "Password Reset Code for Student Badge System";
                
                // Password reset specific HTML email template
                string htmlBody = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                        .header {{ background-color: #e74c3c; color: white; padding: 10px 20px; text-align: center; }}
                        .content {{ padding: 20px; }}
                        .pin {{ font-size: 24px; font-weight: bold; text-align: center; padding: 10px; 
                               background-color: #f0f0f0; margin: 15px 0; letter-spacing: 5px; }}
                        .footer {{ font-size: 12px; text-align: center; margin-top: 20px; color: #666; }}
                        .info {{ background-color: #f8f9fa; padding: 10px; margin: 15px 0; }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>Password Reset Request</h2>
                        </div>
                        <div class='content'>
                            <p>Hello {toName},</p>
                            <p>We received a request to reset your password for the Student Badge System. Your verification code is:</p>
                            <div class='pin'>{resetCode}</div>
                            <p>This code will expire in 10 minutes.</p>
                            <div class='info'>
                                <p><strong>If you didn't request this code,</strong> you can safely ignore this email. Someone else might have typed your username by mistake.</p>
                            </div>
                        </div>
                        <div class='footer'>
                            <p>This is an automated message. Please do not reply to this email.</p>
                            <p>© Student Badge System</p>
                        </div>
                    </div>
                </body>
                </html>";
                
                email.Body = new TextPart(TextFormat.Html) { Text = htmlBody };
                
                _logger.LogInformation("Attempting to connect to SMTP server: smtp.gmail.com:587");
                
                // Send email using the same sending logic as the PIN email
                using (var smtp = new SmtpClient())
                {
                    // Configure timeout
                    smtp.Timeout = 30000; // 30 seconds
                    
                    try
                    {
                        await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                        _logger.LogInformation("Connected to SMTP server successfully");
                        
                        // Try to authenticate
                        _logger.LogInformation("Attempting to authenticate with Gmail");
                        await smtp.AuthenticateAsync(fromEmail, gmailPassword);
                        _logger.LogInformation("Authentication successful");
                        
                        // Send the email
                        _logger.LogInformation("Sending email to {Email}", toEmail);
                        await smtp.SendAsync(email);
                        _logger.LogInformation("Email sent successfully");
                        
                        await smtp.DisconnectAsync(true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Specific error in email sending process");
                        
                        // Provide helpful information about Gmail security settings
                        if (ex.Message.Contains("Authentication") || ex.Message.Contains("5.7.8") || 
                            ex.Message.Contains("5.7.0") || ex.Message.Contains("5.7.14"))
                        {
                            _logger.LogError("Gmail authentication failed. This is likely due to Gmail security settings.");
                            _logger.LogError("Please ensure that:");
                            _logger.LogError("1. The app password is correct (not your regular Gmail password)");
                            _logger.LogError("2. You have enabled 2-factor authentication for your Gmail account");
                            _logger.LogError("3. You have generated an app-specific password at https://myaccount.google.com/apppasswords");
                        }
                        
                        throw; // Re-throw to be caught by the outer catch block
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending password reset email to {Email}: {ErrorMessage}", toEmail, ex.Message);
                return false;
            }
        }
    }
} 