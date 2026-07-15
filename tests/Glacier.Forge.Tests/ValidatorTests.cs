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
}
