using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Glacier.Forge.Backend;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Dynamic Port Binding for Cloud Run
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        builder.WebHost.UseUrls($"http://*:{port}");

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Health check & Website root endpoint
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

        app.MapPost("/webhooks/lemonsqueezy", async (HttpRequest request, IConfiguration config) =>
        {
            // 1. Read the raw body for HMAC validation
            using var reader = new StreamReader(request.Body);
            string rawBody = await reader.ReadToEndAsync();

            // 2. Validate Lemon Squeezy Signature
            string webhookSecret = config["LemonSqueezy:WebhookSecret"] ?? throw new InvalidOperationException("Missing WebhookSecret configuration.");
            string signatureHeader = request.Headers["X-Signature"].ToString();

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret)))
            {
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
                string computedSignature = Convert.ToHexString(hashBytes).ToLowerInvariant();

                byte[] computedBytes = Encoding.UTF8.GetBytes(computedSignature);
                byte[] headerBytes = Encoding.UTF8.GetBytes(signatureHeader);

                if (!CryptographicOperations.FixedTimeEquals(computedBytes, headerBytes))
                {
                    return Results.Unauthorized();
                }
            }

            // 3. Parse the Webhook Payload
            var payload = JsonNode.Parse(rawBody);
            string eventName = payload?["meta"]?["event_name"]?.ToString() ?? "";

            // We only care about new purchases/subscriptions for license generation
            if (eventName is "order_created" or "subscription_created")
            {
                var attributes = payload?["data"]?["attributes"];
                string email = attributes?["user_email"]?.ToString() ?? "unknown@example.com";
                string product = attributes?["product_name"]?.ToString() ?? "Glacier.Forge";
                
                // Logic to determine tier and expiry (e.g., 1 year from now)
                string tier = "Pro"; 
                DateTime subscriptionEnd = DateTime.UtcNow.AddYears(1); 

                // 4. Generate the License Key
                string privateKeyPem = Environment.GetEnvironmentVariable("ECDSA_PRIVATE_KEY") 
                    ?? throw new InvalidOperationException("Missing ECDSA_PRIVATE_KEY environment variable.");
                string licenseKey = GenerateLicenseKey(product, email, tier, subscriptionEnd, privateKeyPem);

                // 5. Deliver the Key
                Console.WriteLine($"Generated Key for {email}: {licenseKey}");
                
                // TODO: Call your email provider here to send the licenseKey to the customer
                // e.g. using SendGrid or SES
            }

            return Results.Ok();
        });

        app.Run();
    }

    private static string GenerateLicenseKey(string product, string email, string tier, DateTime subscriptionEnd, string privateKeyPem)
    {
        // Create the exact JSON structure expected by the CLI
        var licenseData = new
        {
            Product = product,
            Email = email,
            Tier = tier,
            // Format datetime matching yyyy-MM-ddTHH:mm:ssZ
            SubscriptionEnd = subscriptionEnd.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        string jsonPayload = JsonSerializer.Serialize(licenseData);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        // Sign the payload bytes
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        byte[] signatureBytes = ecdsa.SignData(
            payloadBytes, 
            HashAlgorithmName.SHA256, 
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation); // Corrected enum member name for compilation

        // Format as Base64(Payload).Base64(Signature)
        string base64Payload = Convert.ToBase64String(payloadBytes);
        string base64Signature = Convert.ToBase64String(signatureBytes);

        return $"{base64Payload}.{base64Signature}";
    }
}
