using System;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using Glacier.Forge;

namespace Glacier.Forge.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<IngestionBenchmarks>();
    }
}

[MemoryDiagnoser]
public class IngestionBenchmarks
{
    private byte[][] _byteLines = null!;
    private byte[][] _byteLinesWithMapping = null!;
    private string[] _lines = null!;
    private SchemaValidator _validator = null!;
    private SchemaValidator _validatorWithMapping = null!;
    private ColumnSchema[] _columns = null!;

    public class SampleRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Active { get; set; }
        public DateTime Created { get; set; }
    }

    [GlobalSetup]
    public void Setup()
    {
        _columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "name", DataType = "varchar", IsNullable = true, MaxLength = 50 },
            new ColumnSchema { Index = 2, ColumnName = "active", DataType = "boolean", IsNullable = false },
            new ColumnSchema { Index = 3, ColumnName = "created", DataType = "timestamp", IsNullable = false }
        };

        _validator = new SchemaValidator();
        _validator.LoadSchema(_columns);

        var customMappings = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "user_id_field", "id" },
            { "full_name_field", "name" },
            { "active_status", "active" },
            { "creation_timestamp", "created" }
        };

        _validatorWithMapping = new SchemaValidator();
        _validatorWithMapping.LoadSchema(_columns, customMappings);

        // Generate 1000 sample JSONL lines (standard)
        _lines = new string[1000];
        _byteLines = new byte[1000][];
        _byteLinesWithMapping = new byte[1000][];
        
        var rand = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            _lines[i] = $"{{\"id\": {i}, \"name\": \"User-{i}\", \"active\": {(rand.Next(2) == 0 ? "true" : "false")}, \"created\": \"2026-07-14T20:00:00Z\"}}";
            _byteLines[i] = System.Text.Encoding.UTF8.GetBytes(_lines[i]);

            var lineMapped = $"{{\"user_id_field\": {i}, \"full_name_field\": \"User-{i}\", \"active_status\": {(rand.Next(2) == 0 ? "true" : "false")}, \"creation_timestamp\": \"2026-07-14T20:00:00Z\"}}";
            _byteLinesWithMapping[i] = System.Text.Encoding.UTF8.GetBytes(lineMapped);
        }
    }

    [Benchmark]
    public void BenchmarkCustomZeroAllocParser()
    {
        for (int i = 0; i < _byteLines.Length; i++)
        {
            ReadOnlySpan<byte> byteSpan = _byteLines[i];
            var reader = new Utf8JsonReader(byteSpan);
            
            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var key = reader.ValueSpan;
                        if (_validator.TryMatchColumn(key, out var colSchema))
                        {
                            reader.Read();
                            SchemaValidator.IsValidFormat(colSchema, ref reader);
                        }
                        else
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
            }
        }
    }

    [Benchmark]
    public void BenchmarkCustomZeroAllocParserWithMapping()
    {
        for (int i = 0; i < _byteLinesWithMapping.Length; i++)
        {
            ReadOnlySpan<byte> byteSpan = _byteLinesWithMapping[i];
            var reader = new Utf8JsonReader(byteSpan);
            
            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var key = reader.ValueSpan;
                        if (_validatorWithMapping.TryMatchColumn(key, out var colSchema))
                        {
                            reader.Read();
                            SchemaValidator.IsValidFormat(colSchema, ref reader);
                        }
                        else
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
            }
        }
    }

    [Benchmark]
    public void BenchmarkSystemTextJson()
    {
        for (int i = 0; i < _lines.Length; i++)
        {
            var record = JsonSerializer.Deserialize<SampleRecord>(_lines[i]);
        }
    }
}
