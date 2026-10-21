using System;
using Xunit;
using Glacier.Forge;

namespace Glacier.Forge.Tests;

public class ValidatorTests
{
    [Fact]
    public void TestSchemaTrieMatching()
    {
        var validator = new SchemaValidator();
        var columns = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "Id", DataType = "Int", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "UserName", DataType = "Varchar", IsNullable = false, MaxLength = 50 },
            new ColumnSchema { Index = 2, ColumnName = "Created_At", DataType = "Timestamp", IsNullable = true }
        };
        validator.LoadSchema(columns);

        // Case insensitive matching tests
        Assert.True(validator.TryMatchColumn("id"u8, out var col1));
        Assert.Equal(0, col1.Index);
        Assert.Equal("Id", col1.ColumnName);

        Assert.True(validator.TryMatchColumn("USERNAME"u8, out var col2));
        Assert.Equal(1, col2.Index);
        Assert.Equal("UserName", col2.ColumnName);

        Assert.True(validator.TryMatchColumn("Created_At"u8, out var col3));
        Assert.Equal(2, col3.Index);
        Assert.Equal("Created_At", col3.ColumnName);

        // Negative match test
        Assert.False(validator.TryMatchColumn("missing_col"u8, out _));
    }

    private static bool IsValidFormatHelper(ColumnSchema schema, string valueJson)
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes($"{{\"val\": {valueJson}}}");
        var reader = new System.Text.Json.Utf8JsonReader(jsonBytes);
        
        Assert.True(reader.Read()); // StartObject
        Assert.True(reader.Read()); // PropertyName "val"
        Assert.True(reader.Read()); // Value token

        return SchemaValidator.IsValidFormat(schema, ref reader);
    }

    [Fact]
    public void TestFormatValidation()
    {
        var intCol = new ColumnSchema { Index = 0, ColumnName = "Id", DataType = "integer", IsNullable = false };
        var stringCol = new ColumnSchema { Index = 1, ColumnName = "Name", DataType = "character varying", IsNullable = true, MaxLength = 5 };
        var boolCol = new ColumnSchema { Index = 2, ColumnName = "Active", DataType = "boolean", IsNullable = false };
        var dateCol = new ColumnSchema { Index = 3, ColumnName = "Created", DataType = "timestamp", IsNullable = true };

        // Integer format checks
        Assert.True(IsValidFormatHelper(intCol, "123"));
        Assert.False(IsValidFormatHelper(intCol, "\"abc\""));
        Assert.False(IsValidFormatHelper(intCol, "null")); // Not nullable

        // String checks
        Assert.True(IsValidFormatHelper(stringCol, "\"hello\""));
        Assert.False(IsValidFormatHelper(stringCol, "\"hello world\"")); // Too long
        Assert.True(IsValidFormatHelper(stringCol, "null")); // Nullable

        // Boolean checks
        Assert.True(IsValidFormatHelper(boolCol, "true"));
        Assert.True(IsValidFormatHelper(boolCol, "1"));
        Assert.True(IsValidFormatHelper(boolCol, "0"));
        Assert.False(IsValidFormatHelper(boolCol, "\"maybe\""));

        // Date checks
        Assert.True(IsValidFormatHelper(dateCol, "\"2026-07-14\""));
        Assert.False(IsValidFormatHelper(dateCol, "\"not-a-date\""));
    }

    [Fact]
    public void TestPostgresSinkLiteralAndExpansionBuffering()
    {
        var schema = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "is_active", DataType = "boolean", IsNullable = false },
            new ColumnSchema { Index = 2, ColumnName = "description", DataType = "text", IsNullable = true },
            new ColumnSchema { Index = 3, ColumnName = "uuid_val", DataType = "uuid", IsNullable = true }
        };

        var sink = new PostgresSink();
        sink.InitializeAsync("Host=localhost;Database=test", "public.my_table", schema);

        // Record 1: contains boolean literals and explicit nulls
        byte[] line1 = "{\"id\":42, \"is_active\":true, \"uuid_val\":\"00000000-0000-0000-0000-000000000001\"}"u8.ToArray();
        sink.StartRecord();
        sink.WriteField(0, "42"u8, line1);
        sink.WriteField(1, "true"u8, line1); // literal outside line span
        sink.WriteField(2, "null"u8, line1); // null literal
        sink.WriteField(3, "00000000-0000-0000-0000-000000000001"u8, line1);
        sink.EndRecord(commit: true, line1);

        // Record 2: large string to test dynamic buffer growth
        sink.StartRecord();
        sink.WriteField(0, "43"u8, "{\"id\":43}"u8);
        sink.WriteField(1, "false"u8, "{\"id\":43}"u8);
        byte[] largeText = System.Text.Encoding.UTF8.GetBytes(new string('A', 8000));
        sink.WriteField(2, largeText, largeText);
        sink.EndRecord(commit: true, largeText);
    }

    [Fact]
    public void TestSqlServerDataReaderLiteralAndExpansionBuffering()
    {
        var schema = new[]
        {
            new ColumnSchema { Index = 0, ColumnName = "id", DataType = "integer", IsNullable = false },
            new ColumnSchema { Index = 1, ColumnName = "is_active", DataType = "bit", IsNullable = false },
            new ColumnSchema { Index = 2, ColumnName = "notes", DataType = "nvarchar", IsNullable = true }
        };

        var reader = new PooledBatchDataReader(schema, 100);
        reader.Reset(2);

        // Set record 0
        reader.SetValue(0, 0, 101);
        reader.SetValue(1, 0, true);
        reader.SetString(2, 0, "hello world"u8);

        // Set record 1 with large string (tests dynamic buffer growth)
        reader.SetValue(0, 1, 102);
        reader.SetValue(1, 1, false);
        byte[] largeText = System.Text.Encoding.UTF8.GetBytes(new string('Z', 6000));
        reader.SetString(2, 1, largeText);

        Assert.True(reader.Read());
        Assert.Equal(101, reader.GetInt32(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal("hello world", reader.GetString(2));

        Assert.True(reader.Read());
        Assert.Equal(102, reader.GetInt32(0));
        Assert.False(reader.GetBoolean(1));
        Assert.Equal(6000, reader.GetString(2).Length);

        Assert.False(reader.Read());
    }
}
