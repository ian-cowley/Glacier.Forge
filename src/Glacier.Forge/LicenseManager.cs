using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glacier.Forge;

public sealed class LicensePayload
{
    public required string Email { get; init; }
    public required string Tier { get; init; }
    public required DateTime SubscriptionEnd { get; init; }
}

public sealed class ConnectionStringsSection
{
    public string SqlServer { get; set; } = string.Empty;
    public string Postgres { get; set; } = string.Empty;
}

public sealed class AppConfig
{
    public ConnectionStringsSection ConnectionStrings { get; set; } = new();
}

[JsonSerializable(typeof(LicensePayload))]
[JsonSerializable(typeof(AppConfig))]
public partial class LicenseJsonContext : JsonSerializerContext
{
}

public static class LicenseManager
{
    // The ECDSA P-256 Public Key (Hardcoded from backend generator)
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAYi0Rd9z6VfH5oLYXMGB6QBPDA25
        2E7Ip3YFm7BQklK+Pelu4BVlRWYmAGH7YgmKqyiPLSxFuQBr/wRvBOxwJA==
        -----END PUBLIC KEY-----
        """;

    public static bool ValidateLicense(string licenseKey)
    {
        try
        {
            string[] parts = licenseKey.Split('.');
            if (parts.Length != 2)
            {
                Console.Error.WriteLine("License Error: Key format is invalid. Must be 'PayloadBase64.SignatureBase64'.");
                return false;
            }

            byte[] payloadBytes = Convert.FromBase64String(parts[0]);
            byte[] signatureBytes = Convert.FromBase64String(parts[1]);

            // 1. Verify Cryptographic Signature
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);

            bool isSignatureValid = ecdsa.VerifyData(
                payloadBytes, 
                signatureBytes, 
                HashAlgorithmName.SHA256, 
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            if (!isSignatureValid)
            {
                Console.Error.WriteLine("License Error: Cryptographic signature is invalid or tampered.");
                return false;
            }

            // 2. Parse the License Payload (Native AOT Safe)
            var payload = JsonSerializer.Deserialize(
                payloadBytes, 
                LicenseJsonContext.Default.LicensePayload);

            if (payload == null)
            {
                Console.Error.WriteLine("License Error: Unable to deserialize license payload.");
                return false;
            }

            // 3. Extract the Build Date from Assembly Metadata
            DateTime appBuildDate = GetAssemblyBuildDate();

            // 4. The Perpetual Fallback Check
            if (appBuildDate > payload.SubscriptionEnd)
            {
                Console.Error.WriteLine($"License Error: Your subscription ended on {payload.SubscriptionEnd:yyyy-MM-dd}.");
                Console.Error.WriteLine($"This version of the software was built on {appBuildDate:yyyy-MM-dd}.");
                Console.Error.WriteLine("Please renew your license to use this newer version, or download an older version released before your expiry date.");
                return false;
            }

            Console.WriteLine($"License Verified: Valid for {payload.Email} (Tier: {payload.Tier})");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"License Error: Malformed license key. ({ex.Message})");
            return false;
        }
    }

    public static DateTime? OverrideBuildDate { get; set; }

    public static DateTime GetAssemblyBuildDate()
    {
        if (OverrideBuildDate.HasValue)
        {
            return OverrideBuildDate.Value;
        }

        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attribute?.InformationalVersion != null)
        {
            string value = attribute.InformationalVersion;
            int index = value.IndexOf("+build");
            if (index > 0)
            {
                string dateString = value[(index + 6)..];
                if (DateTime.TryParseExact(
                    dateString, 
                    "yyyyMMddHHmmss", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.AssumeUniversal, 
                    out DateTime buildDate))
                {
                    return buildDate;
                }
            }
        }
        // Fallback if compilation metadata is stripped: assume a future date to enforce lockout
        return DateTime.MaxValue; 
    }
}
