using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Glacier.Forge;

namespace Glacier.Forge.Tests;

public class ParserTests
{
    // Ephemeral in-memory key pair generated dynamically at test runtime (NO static private key)
    private static readonly System.Security.Cryptography.ECDsa _testEcdsa = System.Security.Cryptography.ECDsa.Create();

    static ParserTests()
    {
        LicenseManager.PublicKeyPem = _testEcdsa.ExportSubjectPublicKeyInfoPem();
    }

    private static string GenerateLicenseKey(string email, string tier, DateTime subscriptionEnd, string product = "Glacier.Forge")
    {
        var licenseData = new LicensePayload
        {
            Product = product,
            Email = email,
            Tier = tier,
            SubscriptionEnd = subscriptionEnd
        };

        string jsonPayload = System.Text.Json.JsonSerializer.Serialize(
            licenseData, 
            LicenseJsonContext.Default.LicensePayload);

        byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        string payloadBase64 = Convert.ToBase64String(payloadBytes);

        byte[] signatureBytes = _testEcdsa.SignData(
            payloadBytes,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        return $"{payloadBase64}.{signatureBase64}";
    }

    private class MockSink : IRelationalSink
    {
        public bool Started { get; private set; }
        public bool Ended { get; private set; }
        public bool Committed { get; private set; }
        public int WriteCount { get; private set; }

        public Task InitializeAsync(string connectionString, string targetTable, ColumnSchema[] schema) => Task.CompletedTask;
        public void StartRecord() { Started = true; }
        public void WriteField(int colIndex, ReadOnlySpan<byte> value, ReadOnlySpan<byte> line) { WriteCount++; }
        public void EndRecord(bool commit, ReadOnlySpan<byte> line) { Ended = true; Committed = commit; }
        public Task FlushBatchAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void TestSimpleParsing()
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"id\": 101, \"name\": \"John Doe\", \"active\": true, \"data\": null}");
        var reader = new System.Text.Json.Utf8JsonReader(jsonBytes);

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.StartObject, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("id", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.Number, reader.TokenType);
        Assert.Equal("101", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("name", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.String, reader.TokenType);
        Assert.Equal("John Doe", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("active", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.True, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("data", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.Null, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(System.Text.Json.JsonTokenType.EndObject, reader.TokenType);
    }

    [Fact]
    public void TestEscapedStringParsing()
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"message\": \"Hello \\\"World\\\"!\", \"code\": 200}");
        var reader = new System.Text.Json.Utf8JsonReader(jsonBytes);

        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal("message", System.Text.Encoding.UTF8.GetString(reader.ValueSpan));

        Assert.True(reader.Read());
        Assert.Equal("Hello \"World\"!", reader.GetString());
    }

    [Fact]
    public void TestZeroAllocationParsingHotPath()
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"col1\": 1, \"col2\": \"val2\", \"col3\": true}");
        ReadOnlySpan<byte> byteSpan = jsonBytes;

        long beforeAllocations = GC.GetAllocatedBytesForCurrentThread();

        ParseWithoutAllocations(byteSpan);

        long afterAllocations = GC.GetAllocatedBytesForCurrentThread();
        
        Assert.Equal(0, afterAllocations - beforeAllocations);
    }

    private static void ParseWithoutAllocations(ReadOnlySpan<byte> source)
    {
        var reader = new System.Text.Json.Utf8JsonReader(source);
        while (reader.Read())
        {
            _ = reader.TokenType;
            _ = reader.ValueSpan;
        }
    }

    [Fact]
    public void TestIngestionPipelineTolerateUnknownFields()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "name", DataType = "varchar", IsNullable = true }
        };
        validator.LoadSchema(columns);

        var sink = new MockSink();
        string inputJson = "{\"id\": 42, \"extra_ai_thought\": \"some text\", \"name\": \"Alice\"}";
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(inputJson);

        bool success = Program.ProcessRecord(inputBytes, sink, validator, columns);

        Assert.True(success);
        Assert.True(sink.Started);
        Assert.True(sink.Ended);
        Assert.True(sink.Committed);
        Assert.Equal(2, sink.WriteCount);
    }

    private class DetailedMockSink : IRelationalSink
    {
        public System.Collections.Generic.List<(int index, string value)> Fields { get; } = new();
        public bool Started { get; set; }
        public bool Ended { get; set; }
        public bool Committed { get; set; }

        public Task InitializeAsync(string connectionString, string targetTable, ColumnSchema[] schema) => Task.CompletedTask;
        public void StartRecord() => Started = true;
        public void WriteField(int colIndex, ReadOnlySpan<byte> value, ReadOnlySpan<byte> line)
        {
            Fields.Add((colIndex, System.Text.Encoding.UTF8.GetString(value)));
        }
        public void EndRecord(bool commit, ReadOnlySpan<byte> line) { Ended = true; Committed = commit; }
        public Task FlushBatchAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void TestCustomSchemaMapping()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "UserId", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "FullName", DataType = "varchar", IsNullable = true },
            new ColumnSchema { Index = 2, ColumnName = "AccountStatus", DataType = "varchar", IsNullable = true }
        };

        var customMappings = new System.Collections.Generic.Dictionary<string, string>
        {
            { "user_id_val", "UserId" },
            { "name_from_llm", "FullName" },
            { "status_code", "AccountStatus" }
        };

        validator.LoadSchema(columns, customMappings);

        var sink = new DetailedMockSink();
        string inputJson = "{\"user_id_val\": 101, \"name_from_llm\": \"ValB\", \"status_code\": \"ValC\"}";
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(inputJson);

        bool success = Program.ProcessRecord(inputBytes, sink, validator, columns);

        Assert.True(success);
        Assert.True(sink.Committed);
        Assert.Equal(3, sink.Fields.Count);

        Assert.Contains(sink.Fields, f => f.index == 0 && f.value == "101");
        Assert.Contains(sink.Fields, f => f.index == 1 && f.value == "ValB");
        Assert.Contains(sink.Fields, f => f.index == 2 && f.value == "ValC");
    }

    [Fact]
    public void TestIngestionPipelineRejectFormatMismatch()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false }
        };
        validator.LoadSchema(columns);

        var sink = new MockSink();
        string inputJson = "{\"id\": \"not_an_int\"}";
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(inputJson);

        bool success = Program.ProcessRecord(inputBytes, sink, validator, columns);

        Assert.False(success);
        Assert.True(sink.Started);
        Assert.True(sink.Ended);
        Assert.False(sink.Committed);
    }

    [Fact]
    public void TestIngestionPipelineConvertibleTypes()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "price", DataType = "decimal", IsNullable = true },
            new ColumnSchema { Index = 2, ColumnName = "ratio", DataType = "double precision", IsNullable = true }
        };
        validator.LoadSchema(columns);

        var sink = new DetailedMockSink();
        // 1. "id" is string "42" (convertible to integer)
        // 2. "price" is string "12.34" (convertible to decimal)
        // 3. "ratio" is number 100 (integer convertible to double)
        string inputJson = "{\"id\": \"42\", \"price\": \"12.34\", \"ratio\": 100}";
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(inputJson);

        bool success = Program.ProcessRecord(inputBytes, sink, validator, columns);

        Assert.True(success);
        Assert.True(sink.Committed);
        Assert.Equal(3, sink.Fields.Count);

        // Verify values are parsed correctly
        Assert.Contains(sink.Fields, f => f.index == 0 && f.value == "42");
        Assert.Contains(sink.Fields, f => f.index == 1 && f.value == "12.34");
        Assert.Contains(sink.Fields, f => f.index == 2 && f.value == "100");
    }

    [Fact]
    public void TestIngestionPipelineUnconvertibleTypes()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false }
        };
        validator.LoadSchema(columns);

        var sink = new MockSink();
        
        // 1. Lossy conversion (float 42.5 in integer column)
        string inputJsonLossy = "{\"id\": 42.5}";
        byte[] inputBytesLossy = System.Text.Encoding.UTF8.GetBytes(inputJsonLossy);
        bool successLossy = Program.ProcessRecord(inputBytesLossy, sink, validator, columns);
        Assert.False(successLossy);

        // 2. Completely unconvertible text in integer column
        string inputJsonUnconvertible = "{\"id\": \"abc\"}";
        byte[] inputBytesUnconvertible = System.Text.Encoding.UTF8.GetBytes(inputJsonUnconvertible);
        bool successUnconvertible = Program.ProcessRecord(inputBytesUnconvertible, sink, validator, columns);
        Assert.False(successUnconvertible);
    }

    [Fact]
    public void TestIngestionPipelineRejectMissingNonNullable()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "name", DataType = "varchar", IsNullable = true }
        };
        validator.LoadSchema(columns);

        var sink = new MockSink();
        string inputJson = "{\"name\": \"Alice\"}";
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(inputJson);

        bool success = Program.ProcessRecord(inputBytes, sink, validator, columns);

        Assert.False(success);
        Assert.True(sink.Started);
        Assert.True(sink.Ended);
        Assert.False(sink.Committed);
    }

    [Fact]
    public void TestLicenseValidationSuccess()
    {
        LicenseManager.OverrideBuildDate = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        
        string licenseKey = GenerateLicenseKey("user@domain.com", "enterprise", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        bool isValid = LicenseManager.ValidateLicense(licenseKey);
        Assert.True(isValid);
    }

    [Fact]
    public void TestLicenseValidationExpired()
    {
        LicenseManager.OverrideBuildDate = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        
        string licenseKey = GenerateLicenseKey("user@domain.com", "enterprise", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        bool isValid = LicenseManager.ValidateLicense(licenseKey);
        Assert.False(isValid);
    }

    [Fact]
    public void TestLicenseValidationTampered()
    {
        LicenseManager.OverrideBuildDate = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        string licenseKey = GenerateLicenseKey("user@domain.com", "enterprise", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        string[] parts = licenseKey.Split('.');
        char[] signatureChars = parts[1].ToCharArray();
        signatureChars[0] = signatureChars[0] == 'A' ? 'B' : 'A';
        string tamperedKey = $"{parts[0]}.{new string(signatureChars)}";

        bool isValid = LicenseManager.ValidateLicense(tamperedKey);
        Assert.False(isValid);
    }

    [Fact]
    public void TestLicenseValidationMissingFields()
    {
        LicenseManager.OverrideBuildDate = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);

        var payload = new { Email = "user@domain.com", Tier = "Pro" };
        string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        byte[] payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadJson);
        string payloadBase64 = Convert.ToBase64String(payloadBytes);

        byte[] signatureBytes = _testEcdsa.SignData(
            payloadBytes,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        string signatureBase64 = Convert.ToBase64String(signatureBytes);
        string malformedKey = $"{payloadBase64}.{signatureBase64}";

        bool isValid = LicenseManager.ValidateLicense(malformedKey);
        Assert.False(isValid);
    }

    [Fact]
    public void TestLicenseValidationProductMismatch()
    {
        LicenseManager.OverrideBuildDate = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        
        // Key is cryptographically valid, but generated for a different product name
        string licenseKey = GenerateLicenseKey("user@domain.com", "enterprise", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Glacier.Polaris");
        
        bool isValid = LicenseManager.ValidateLicense(licenseKey);
        Assert.False(isValid);
    }

    [Fact]
    public async Task TestIngestFileAsync()
    {
        string tempFilePath = System.IO.Path.GetTempFileName();
        try
        {
            string[] lines =
            [
                "{\"id\": 10, \"name\": \"Bob\", \"active\": true, \"created\": \"2026-07-14T20:00:00Z\"}",
                "{\"id\": 11, \"name\": \"Charlie\", \"active\": false, \"created\": \"2026-07-14T21:00:00Z\"}",
                "{\"id\": \"invalid_id\", \"name\": \"Hacker\", \"active\": true, \"created\": \"2026-07-14T22:00:00Z\"}"
            ];
            System.IO.File.WriteAllLines(tempFilePath, lines);

            var columns = new[]
            {
                new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
                new ColumnSchema { Index = 1, ColumnName = "name", DataType = "varchar", IsNullable = true },
                new ColumnSchema { Index = 2, ColumnName = "active", DataType = "boolean", IsNullable = false },
                new ColumnSchema { Index = 3, ColumnName = "created", DataType = "timestamp", IsNullable = false }
            };

            var validator = new SchemaValidator();
            validator.LoadSchema(columns);

            var sink = new MockSink();
            await sink.InitializeAsync("", "TestTable", columns);

            var method = typeof(Program).GetMethod("IngestFileAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            var task = (Task)method.Invoke(null, [tempFilePath, sink, validator, columns])!;
            await task;

            Assert.True(sink.Started);
            Assert.True(sink.Ended);
            Assert.Equal(8, sink.WriteCount);
        }
        finally
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                System.IO.File.Delete(tempFilePath);
            }
        }
    }
}
