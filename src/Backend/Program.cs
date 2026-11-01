using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glacier.Forge.Backend;

public interface ILicenseDeliveryService
{
    Task DeliverAsync(string email, string product, string tier, string licenseKey, CancellationToken cancellationToken = default);
}

public sealed class SmtpLicenseDeliveryService : ILicenseDeliveryService
{
    public async Task DeliverAsync(string email, string product, string tier, string licenseKey, CancellationToken cancellationToken = default)
    {
        string? smtpHost = Environment.GetEnvironmentVariable("FORGE_SMTP_HOST");
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new InvalidOperationException("License delivery provider is not configured (missing FORGE_SMTP_HOST).");
        }

        using var client = new SmtpClient(smtpHost)
        {
            Timeout = 15_000,
            EnableSsl = !string.Equals(Environment.GetEnvironmentVariable("FORGE_SMTP_ENABLE_SSL"), "false", StringComparison.OrdinalIgnoreCase)
        };

        string? portStr = Environment.GetEnvironmentVariable("FORGE_SMTP_PORT");
        if (int.TryParse(portStr, out int port) && port > 0)
        {
            client.Port = port;
        }

        string? user = Environment.GetEnvironmentVariable("FORGE_SMTP_USER");
        string? pass = Environment.GetEnvironmentVariable("FORGE_SMTP_PASSWORD");
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
        {
            client.Credentials = new NetworkCredential(user, pass);
        }

        string from = Environment.GetEnvironmentVariable("FORGE_SMTP_FROM") ?? "licenses@glacier.dev";
        using var msg = new MailMessage(from, email)
        {
            Subject = $"Your {product} License Key ({tier})",
            Body = $"Thank you for your order!\n\nYour license key for {product} ({tier}) is:\n\n{licenseKey}\n\nKeep this key secure."
        };

        await client.SendMailAsync(msg, cancellationToken);
    }
}

public sealed class BackendLicensePayload
{
    public string Product { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string SubscriptionEnd { get; set; } = string.Empty;
}

[JsonSerializable(typeof(BackendLicensePayload))]
internal partial class BackendJsonContext : JsonSerializerContext
{
}

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        builder.WebHost.UseUrls($"http://*:{port}");
        builder.Services.AddSingleton<ILicenseDeliveryService, SmtpLicenseDeliveryService>();

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapMethods("/", new[] { "GET", "HEAD" }, () =>
        {
            var filePath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
            if (File.Exists(filePath))
            {
                return Results.File(filePath, "text/html");
            }
            return Results.Ok("Glacier API Online");
        });

        app.MapMethods("/health", new[] { "GET", "HEAD" }, () => Results.Ok("Glacier API Online"));

        app.MapPost("/webhooks/lemonsqueezy", (HttpRequest request, IConfiguration config, ILicenseDeliveryService deliveryService, CancellationToken ct) =>
            HandleLemonSqueezyWebhookAsync(request, config, deliveryService, Environment.GetEnvironmentVariable("ECDSA_PRIVATE_KEY"), ct));

        app.Run();
    }

    internal static bool IsValidWebhookSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Trim().Length < 16)
        {
            return false;
        }

        string trimmed = secret.Trim();
        if (trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("SECRET_HERE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    internal static async Task<IResult> HandleLemonSqueezyWebhookAsync(
        HttpRequest request,
        IConfiguration config,
        ILicenseDeliveryService deliveryService,
        string? privateKeyPem,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate WebhookSecret configuration fail-closed (never accept empty or placeholder secrets)
        string? webhookSecret = Environment.GetEnvironmentVariable("FORGE_WEBHOOK_SECRET");
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            webhookSecret = config["LemonSqueezy:WebhookSecret"];
        }

        if (!IsValidWebhookSecret(webhookSecret))
        {
            Console.Error.WriteLine("[Webhook] Rejecting request: WebhookSecret is missing, too short, or set to a placeholder.");
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        // 2. Read raw body & verify HMAC-SHA256 signature in constant time
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        string rawBody = await reader.ReadToEndAsync(cancellationToken);

        string signatureHeader = request.Headers["X-Signature"].ToString().Trim();
        if (signatureHeader.Length != 64)
        {
            return Results.Unauthorized();
        }

        byte[] providedSigBytes;
        try
        {
            providedSigBytes = Convert.FromHexString(signatureHeader);
        }
        catch (FormatException)
        {
            return Results.Unauthorized();
        }

        byte[] computedSigBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(webhookSecret!),
            Encoding.UTF8.GetBytes(rawBody));

        if (!CryptographicOperations.FixedTimeEquals(computedSigBytes, providedSigBytes))
        {
            return Results.Unauthorized();
        }

        // 3. Parse Webhook Payload
        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        string eventName = payload?["meta"]?["event_name"]?.ToString() ?? string.Empty;

        if (eventName is "order_created" or "subscription_created")
        {
            var attributes = payload?["data"]?["attributes"];
            string? email = attributes?["user_email"]?.ToString();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Results.BadRequest();
            }

            string product = attributes?["product_name"]?.ToString() ?? "Glacier.Forge";
            string tier = "Pro";
            DateTime subscriptionEnd = DateTime.UtcNow.AddYears(1);

            if (string.IsNullOrWhiteSpace(privateKeyPem))
            {
                Console.Error.WriteLine("[Webhook] Missing ECDSA_PRIVATE_KEY environment variable.");
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            string licenseKey = GenerateLicenseKey(product, email, tier, subscriptionEnd, privateKeyPem);

            string maskedKey = licenseKey.Length > 16
                ? $"{licenseKey[..8]}...{licenseKey[^8..]}"
                : "[REDACTED]";
            Console.WriteLine($"Generated Key for {email}: {maskedKey}");

            try
            {
                await deliveryService.DeliverAsync(email, product, tier, licenseKey, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[License Delivery] Failed delivering license to {email}: {ex.Message}");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        return Results.Ok();
    }

    internal static string GenerateLicenseKey(string product, string email, string tier, DateTime subscriptionEnd, string privateKeyPem)
    {
        var licenseData = new BackendLicensePayload
        {
            Product = product,
            Email = email,
            Tier = tier,
            SubscriptionEnd = subscriptionEnd.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        string jsonPayload = JsonSerializer.Serialize(licenseData, BackendJsonContext.Default.BackendLicensePayload);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        byte[] signatureBytes = ecdsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        string base64Payload = Convert.ToBase64String(payloadBytes);
        string base64Signature = Convert.ToBase64String(signatureBytes);

        return $"{base64Payload}.{base64Signature}";
    }
}
