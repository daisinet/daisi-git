using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace DaisiGit.Services;

/// <summary>
/// Sends emails from the DaisiGit platform. Configured once at startup
/// with SMTP credentials — individual workflows do not need their own SMTP config.
/// </summary>
public class EmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _pass;
    private readonly string _from;
    private readonly string _fromName;
    private readonly bool _enabled;

    public EmailService(IConfiguration configuration)
    {
        _host = configuration["Email:SmtpHost"] ?? "";
        _port = int.TryParse(configuration["Email:SmtpPort"], out var p) ? p : 587;
        _user = configuration["Email:SmtpUser"] ?? "";
        _pass = configuration["Email:SmtpPass"] ?? "";
        _from = configuration["Email:From"] ?? "noreply@git.daisi.ai";
        _fromName = configuration["Email:FromName"] ?? "Daisi Git";
        _enabled = !string.IsNullOrEmpty(_host) && !string.IsNullOrEmpty(_user);
    }

    public bool IsEnabled => _enabled;

    /// <summary>
    /// Sends an email from the DaisiGit platform.
    /// </summary>
    public async Task SendAsync(string to, string subject, string body)
    {
        if (!_enabled)
            throw new InvalidOperationException("Email is not configured. Set Email:SmtpHost, Email:SmtpUser, and Email:SmtpPass in app settings.");

        using var client = new SmtpClient(_host, _port)
        {
            Credentials = new NetworkCredential(_user, _pass),
            EnableSsl = true,
            Timeout = 30000
        };

        var from = new MailAddress(_from, _fromName);
        var message = new MailMessage { From = from, Subject = subject, Body = body };
        message.IsBodyHtml = body.Contains('<');

        foreach (var addr in to.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            message.To.Add(addr);

        await client.SendMailAsync(message);
    }
}
