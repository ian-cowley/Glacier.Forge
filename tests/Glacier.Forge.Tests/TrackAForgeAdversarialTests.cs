using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Glacier.Forge;
using Glacier.Forge.Backend;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Glacier.Forge.Tests;

[Collection("LicenseTests")]
public class TrackAForgeAdversarialTests
{
    private static ColumnSchema[] CreateAdversarialSchema() =>
    [
        new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
        new ColumnSchema { Index = 1, ColumnName = "escaped_text", DataType = "varchar", IsNullable = false, MaxLength = 100 },
        new ColumnSchema { Index = 2, ColumnName = "empty_required", DataType = "varchar", IsNullable = false, MaxLength = 50 },
        new ColumnSchema { Index = 3, ColumnName = "literal_word", DataType = "varchar", IsNullable = false, MaxLength = 20 },
        new ColumnSchema { Index = 4, ColumnName = "nullable_note", DataType = "text", IsNullable = true },
        new ColumnSchema { Index = 5, ColumnName = "active", DataType = "boolean", IsNullable = false },
        new ColumnSchema { Index = 6, ColumnName = "payload", DataType = "jsonb", IsNullable = false }
    ];

    [Fact]
    public async Task ProcessRecord_RealPostgresSink_PreservesEscapesEmptyLiteralsAndJsonb()
    {
        var schema = CreateAdversarialSchema();
        var validator = new SchemaValidator();
        validator.LoadSchema(schema);

        var sink = new PostgresSink();
        await sink.InitializeAsync("Host=localhost;Database=test", "public.events", schema);

        // Row 0: escaped quotes/newlines/unicode, escaped property name ("n\u0075llable_note"), empty string "", literal "null", nested jsonb object
        byte[] row0 = Encoding.UTF8.GetBytes(
            "{\"id\":1,\"escaped_text\":\"Hello \\\"Glacier\\\"\\n\\u0041!\",\"empty_required\":\"\",\"literal_word\":\"null\",\"n\\u0075llable_note\":null,\"active\":true,\"payload\":{\"nested\":[1,2,true],\"tag\":\"ok\"}}");
        Assert.True(Program.ProcessRecord(row0, sink, validator, schema));

        // Row 1: literal "true" and "false" strings in varchar, array in jsonb
        byte[] row1 = Encoding.UTF8.GetBytes(
            "{\"id\":2,\"escaped_text\":\"true\",\"empty_required\":\"\",\"literal_word\":\"false\",\"nullable_note\":\"present\",\"active\":false,\"payload\":[{\"k\":\"v\"},42]}");
        Assert.True(Program.ProcessRecord(row1, sink, validator, schema));

        Assert.Equal(2, sink.BufferedRecordCount);

        // Row 0 assertions
        Assert.Equal(1, sink.GetBufferedInt32(0, 0));
        Assert.False(sink.IsBufferedNull(1, 0));
        Assert.Equal("Hello \"Glacier\"\nA!", sink.GetBufferedString(1, 0));
        Assert.False(sink.IsBufferedNull(2, 0));
        Assert.Equal(string.Empty, sink.GetBufferedString(2, 0));
        Assert.False(sink.IsBufferedNull(3, 0));
        Assert.Equal("null", sink.GetBufferedString(3, 0));
        Assert.True(sink.IsBufferedNull(4, 0));
        Assert.True(sink.GetBufferedBoolean(5, 0));
        Assert.Equal("{\"nested\":[1,2,true],\"tag\":\"ok\"}", sink.GetBufferedString(6, 0));

        // Row 1 assertions
        Assert.Equal(2, sink.GetBufferedInt32(0, 1));
        Assert.Equal("true", sink.GetBufferedString(1, 1));
        Assert.False(sink.IsBufferedNull(2, 1));
        Assert.Equal(string.Empty, sink.GetBufferedString(2, 1));
        Assert.Equal("false", sink.GetBufferedString(3, 1));
        Assert.Equal("present", sink.GetBufferedString(4, 1));
        Assert.False(sink.GetBufferedBoolean(5, 1));
        Assert.Equal("[{\"k\":\"v\"},42]", sink.GetBufferedString(6, 1));
    }

    [Fact]
    public void ProcessRecord_RealSqlServerSink_PreservesEscapesEmptyLiteralsAndJsonb()
    {
        var schema = CreateAdversarialSchema();
        var validator = new SchemaValidator();
        validator.LoadSchema(schema);

        var sink = new SqlServerSink();
        sink.InitializeBuffersForTesting(schema);

        byte[] row0 = Encoding.UTF8.GetBytes(
            "{\"id\":10,\"escaped_text\":\"Quote: \\\"X\\\"\\tTab\",\"empty_required\":\"\",\"literal_word\":\"true\",\"nullable_note\":null,\"active\":true,\"payload\":{\"a\":1,\"b\":\"two\"}}");
        Assert.True(Program.ProcessRecord(row0, sink, validator, schema));

        byte[] row1 = Encoding.UTF8.GetBytes(
            "{\"id\":11,\"escaped_text\":\"\\u0047\\u006c\\u0061\\u0063\\u0069\\u0065\\u0072\",\"empty_required\":\"\",\"literal_word\":\"null\",\"nullable_note\":\"ok\",\"active\":false,\"payload\":[1,2,3]}");
        Assert.True(Program.ProcessRecord(row1, sink, validator, schema));

        Assert.Equal(2, sink.BufferedRecordCount);
        sink.DataReader.Reset(sink.BufferedRecordCount);

        Assert.True(sink.DataReader.Read());
        Assert.Equal(10, sink.DataReader.GetInt32(0));
        Assert.False(sink.DataReader.IsDBNull(1));
        Assert.Equal("Quote: \"X\"\tTab", sink.DataReader.GetString(1));
        Assert.False(sink.DataReader.IsDBNull(2));
        Assert.Equal(string.Empty, sink.DataReader.GetString(2));
        Assert.False(sink.DataReader.IsDBNull(3));
        Assert.Equal("true", sink.DataReader.GetString(3));
        Assert.True(sink.DataReader.IsDBNull(4));
        Assert.True(sink.DataReader.GetBoolean(5));
        Assert.Equal("{\"a\":1,\"b\":\"two\"}", sink.DataReader.GetString(6));

        Assert.True(sink.DataReader.Read());
        Assert.Equal(11, sink.DataReader.GetInt32(0));
        Assert.Equal("Glacier", sink.DataReader.GetString(1));
        Assert.False(sink.DataReader.IsDBNull(2));
        Assert.Equal(string.Empty, sink.DataReader.GetString(2));
        Assert.Equal("null", sink.DataReader.GetString(3));
        Assert.Equal("ok", sink.DataReader.GetString(4));
        Assert.False(sink.DataReader.GetBoolean(5));
        Assert.Equal("[1,2,3]", sink.DataReader.GetString(6));

        Assert.False(sink.DataReader.Read());
    }

    [Fact]
    public async Task ProcessRecord_EscapedStringsAndJsonb_ZeroSteadyStateHeapAllocations()
    {
        var schema = CreateAdversarialSchema();
        var validator = new SchemaValidator();
        validator.LoadSchema(schema);

        var pgSink = new PostgresSink();
        await pgSink.InitializeAsync("Host=localhost;Database=test", "public.events", schema);

        var sqlSink = new SqlServerSink();
        sqlSink.InitializeBuffersForTesting(schema);

        byte[] line = Encoding.UTF8.GetBytes(
            "{\"id\":7,\"escaped_text\":\"Line1\\n\\\"Line2\\\"\\u0021\",\"empty_required\":\"\",\"literal_word\":\"null\",\"nullable_note\":null,\"active\":true,\"payload\":{\"k\":123}}");

        // Warmup JIT & pre-sized buffers
        for (int i = 0; i < 5; i++)
        {
            Assert.True(Program.ProcessRecord(line, pgSink, validator, schema));
            Assert.True(Program.ProcessRecord(line, sqlSink, validator, schema));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            bool okPg = Program.ProcessRecord(line, pgSink, validator, schema);
            bool okSql = Program.ProcessRecord(line, sqlSink, validator, schema);
            if (!okPg || !okSql)
            {
                throw new InvalidOperationException("Unexpected record failure.");
            }
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void SchemaValidator_ValidatesCharNcharEscapedMaxLengthAndRejectsNestedObjectsOnStrings()
    {
        var charCol = new ColumnSchema { Index = 0, ColumnName = "code", DataType = "char", IsNullable = false, MaxLength = 3 };
        var ncharCol = new ColumnSchema { Index = 1, ColumnName = "ncode", DataType = "nchar", IsNullable = false, MaxLength = 3 };
        var jsonCol = new ColumnSchema { Index = 2, ColumnName = "doc", DataType = "jsonb", IsNullable = false };
        var schema = new[] { charCol, ncharCol, jsonCol };

        var validator = new SchemaValidator();
        validator.LoadSchema(schema);

        var sink = new PostgresSink();
        sink.InitializeAsync("Host=localhost;Database=test", "public.codes", schema);

        // "\u0041\u0042\u0043" is 18 raw UTF-8 bytes in JSON but 3 unescaped chars -> valid for MaxLength = 3
        byte[] validEscaped = Encoding.UTF8.GetBytes(
            "{\"code\":\"\\u0041\\u0042\\u0043\",\"ncode\":\"XYZ\",\"doc\":{\"ok\":true}}");
        Assert.True(Program.ProcessRecord(validEscaped, sink, validator, schema));
        Assert.Equal("ABC", sink.GetBufferedString(0, 0));

        // 4 unescaped chars -> rejected for MaxLength = 3
        byte[] tooLongChar = Encoding.UTF8.GetBytes(
            "{\"code\":\"ABCD\",\"ncode\":\"XYZ\",\"doc\":{\"ok\":true}}");
        Assert.False(Program.ProcessRecord(tooLongChar, sink, validator, schema));

        // 4 escaped chars -> rejected for nchar(3)
        byte[] tooLongEscaped = Encoding.UTF8.GetBytes(
            "{\"code\":\"ABC\",\"ncode\":\"\\u0041\\u0042\\u0043\\u0044\",\"doc\":{\"ok\":true}}");
        Assert.False(Program.ProcessRecord(tooLongEscaped, sink, validator, schema));

        // Nested JSON object supplied for char/varchar column -> rejected
        byte[] objectOnString = Encoding.UTF8.GetBytes(
            "{\"code\":{\"bad\":1},\"ncode\":\"XYZ\",\"doc\":{\"ok\":true}}");
        Assert.False(Program.ProcessRecord(objectOnString, sink, validator, schema));

        // Malformed/truncated nested jsonb object -> rejected
        byte[] malformedJsonb = Encoding.UTF8.GetBytes(
            "{\"code\":\"ABC\",\"ncode\":\"XYZ\",\"doc\":{\"unclosed\":[1,2}");
        Assert.False(Program.ProcessRecord(malformedJsonb, sink, validator, schema));
    }

    private sealed class CapturingDeliveryService : ILicenseDeliveryService
    {
        public bool ShouldFail { get; set; }
        public string? LastEmail { get; private set; }
        public string? LastLicenseKey { get; private set; }

        public Task DeliverAsync(string email, string product, string tier, string licenseKey, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated SMTP outage.");
            }
            LastEmail = email;
            LastLicenseKey = licenseKey;
            return Task.CompletedTask;
        }
    }

    private static async Task<int> ExecuteResultStatusCodeAsync(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext
        {
            RequestServices = provider
        };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        return ctx.Response.StatusCode;
    }

    private static DefaultHttpContext CreateSignedWebhookContext(string body, string signingSecret)
    {
        var ctx = new DefaultHttpContext();
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        ctx.Request.Body = new MemoryStream(bodyBytes);
        byte[] hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), bodyBytes);
        ctx.Request.Headers["X-Signature"] = Convert.ToHexString(hmac).ToLowerInvariant();
        return ctx;
    }

    [Fact]
    public async Task Webhook_RejectsPlaceholderSecretForgedSignatureAndDeliveryFailure_SucceedsEndToEnd()
    {
        const string payloadJson = "{\"meta\":{\"event_name\":\"order_created\"},\"data\":{\"attributes\":{\"user_email\":\"buyer@example.com\",\"product_name\":\"Glacier.Forge\"}}}";

        using var ephemeralEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privatePem = ephemeralEcdsa.ExportECPrivateKeyPem();
        string publicPem = ephemeralEcdsa.ExportSubjectPublicKeyInfoPem();

        string? prevEnvSecret = Environment.GetEnvironmentVariable("FORGE_WEBHOOK_SECRET");
        string? prevSmtpHost = Environment.GetEnvironmentVariable("FORGE_SMTP_HOST");
        string prevPublicKey = LicenseManager.PublicKeyPem;
        DateTime? prevBuildDate = LicenseManager.OverrideBuildDate;

        try
        {
            Environment.SetEnvironmentVariable("FORGE_WEBHOOK_SECRET", null);
            Environment.SetEnvironmentVariable("FORGE_SMTP_HOST", null);

            // 1. Placeholder secret MUST fail closed with 500 even when caller signs with the placeholder
            const string placeholder = "YOUR_LEMON_SQUEEZY_WEBHOOK_SECRET_HERE";
            var placeholderConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LemonSqueezy:WebhookSecret"] = placeholder })
                .Build();
            var ctx1 = CreateSignedWebhookContext(payloadJson, placeholder);
            var delivery = new CapturingDeliveryService();
            int status1 = await ExecuteResultStatusCodeAsync(
                await Glacier.Forge.Backend.Program.HandleLemonSqueezyWebhookAsync(ctx1.Request, placeholderConfig, delivery, privatePem));
            Assert.Equal(StatusCodes.Status500InternalServerError, status1);

            // 2. Forged signature MUST fail with 401 Unauthorized
            const string validSecret = "whsec_live_9876543210abcdef9876543210";
            var validConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LemonSqueezy:WebhookSecret"] = validSecret })
                .Build();
            var ctx2 = CreateSignedWebhookContext(payloadJson, "wrong_secret_value_00000000000000");
            int status2 = await ExecuteResultStatusCodeAsync(
                await Glacier.Forge.Backend.Program.HandleLemonSqueezyWebhookAsync(ctx2.Request, validConfig, delivery, privatePem));
            Assert.Equal(StatusCodes.Status401Unauthorized, status2);

            // 3. Unconfigured SmtpLicenseDeliveryService MUST return 503 ServiceUnavailable so webhook retries
            var ctx3 = CreateSignedWebhookContext(payloadJson, validSecret);
            var realSmtpService = new SmtpLicenseDeliveryService();
            int status3 = await ExecuteResultStatusCodeAsync(
                await Glacier.Forge.Backend.Program.HandleLemonSqueezyWebhookAsync(ctx3.Request, validConfig, realSmtpService, privatePem));
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, status3);

            // 4. Successful delivery returns 200 OK and emits a cryptographically valid license key
            var ctx4 = CreateSignedWebhookContext(payloadJson, validSecret);
            int status4 = await ExecuteResultStatusCodeAsync(
                await Glacier.Forge.Backend.Program.HandleLemonSqueezyWebhookAsync(ctx4.Request, validConfig, delivery, privatePem));
            Assert.Equal(StatusCodes.Status200OK, status4);
            Assert.Equal("buyer@example.com", delivery.LastEmail);
            Assert.False(string.IsNullOrWhiteSpace(delivery.LastLicenseKey));

            LicenseManager.PublicKeyPem = publicPem;
            LicenseManager.OverrideBuildDate = DateTime.UtcNow;
            Assert.True(LicenseManager.ValidateLicense(delivery.LastLicenseKey!));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGE_WEBHOOK_SECRET", prevEnvSecret);
            Environment.SetEnvironmentVariable("FORGE_SMTP_HOST", prevSmtpHost);
            LicenseManager.PublicKeyPem = ParserTests.DefaultTestPublicKeyPem;
            LicenseManager.OverrideBuildDate = prevBuildDate;
        }
    }
}
