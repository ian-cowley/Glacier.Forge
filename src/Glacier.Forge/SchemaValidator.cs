using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Glacier.Forge;

public enum DatabaseType 
{ 
    SqlServer, 
    Postgres 
}

public sealed class ColumnSchema
{
    private string _dataType = string.Empty;
    public int Index { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    
    public string DataType 
    { 
        get => _dataType; 
        set 
        { 
            _dataType = value; 
            DataTypeLower = value.ToLowerInvariant(); 
        } 
    }
    
    public string DataTypeLower { get; private set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int MaxLength { get; set; }
}

public sealed class SchemaValidator
{
    private readonly TrieNode _root = new();

    public void LoadSchema(IEnumerable<ColumnSchema> columns, Dictionary<string, string>? customMappings = null)
    {
        var columnLookup = new Dictionary<string, ColumnSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            columnLookup[col.ColumnName] = col;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(col.ColumnName);
            Insert(bytes, col);
        }

        if (customMappings != null)
        {
            foreach (var (jsonKey, dbColumnName) in customMappings)
            {
                if (columnLookup.TryGetValue(dbColumnName, out var colSchema))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonKey);
                    Insert(bytes, colSchema);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToUpperAscii(byte b)
    {
        if (b >= 97 && b <= 122)
        {
            return (byte)(b - 32);
        }
        return b;
    }

    private void Insert(ReadOnlySpan<byte> columnName, ColumnSchema schema)
    {
        TrieNode current = _root;
        foreach (byte b in columnName)
        {
            byte upper = ToUpperAscii(b);
            var next = current.Children[upper];
            if (next == null)
            {
                next = new TrieNode();
                current.Children[upper] = next;
            }
            current = next;
        }
        current.Schema = schema;
    }

    public bool TryMatchColumn(ReadOnlySpan<byte> columnNameSpan, out ColumnSchema schema)
    {
        TrieNode current = _root;
        foreach (byte b in columnNameSpan)
        {
            byte upper = ToUpperAscii(b);
            var next = current.Children[upper];
            if (next == null)
            {
                schema = null!;
                return false;
            }
            current = next;
        }
        schema = current.Schema!;
        return schema != null;
    }

    public static bool IsValidFormat(ColumnSchema schema, ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return schema.IsNullable;
        }

        switch (schema.DataTypeLower)
        {
            case "int":
            case "integer":
            case "int4":
                if (reader.TokenType == JsonTokenType.Number)
                {
                    return reader.TryGetInt32(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out int _, out _);

            case "smallint":
            case "int2":
                if (reader.TokenType == JsonTokenType.Number)
                {
                    return reader.TryGetInt16(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out short _, out _);

            case "bigint":
            case "int8":
                if (reader.TokenType == JsonTokenType.Number)
                {
                    return reader.TryGetInt64(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out long _, out _);

            case "uuid":
            case "uniqueidentifier":
            case "guid":
                if (reader.TokenType == JsonTokenType.String)
                {
                    return reader.TryGetGuid(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out Guid _, out _);

            case "varchar":
            case "nvarchar":
            case "text":
            case "character varying":
                if (schema.MaxLength <= 0) return true;
                int charCount = System.Text.Encoding.UTF8.GetCharCount(reader.ValueSpan);
                return charCount <= schema.MaxLength;

            case "datetime":
            case "datetime2":
            case "timestamp":
            case "timestamp without time zone":
            case "timestamp with time zone":
            case "timestamptz":
            case "date":
                if (reader.TokenType == JsonTokenType.String || reader.TokenType == JsonTokenType.PropertyName)
                {
                    return reader.TryGetDateTime(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out DateTime _, out _, 'O') ||
                       System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out DateTime _, out _);

            case "json":
            case "jsonb":
                return true;

            case "boolean":
            case "bool":
            case "bit":
                if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                {
                    return true;
                }
                var span = reader.ValueSpan;
                if (span.SequenceEqual("true"u8) || span.SequenceEqual("1"u8)) return true;
                if (span.SequenceEqual("false"u8) || span.SequenceEqual("0"u8)) return true;
                return System.Buffers.Text.Utf8Parser.TryParse(span, out bool _, out _);

            case "float":
            case "double precision":
                if (reader.TokenType == JsonTokenType.Number)
                {
                    return reader.TryGetDouble(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out double _, out _);

            case "decimal":
            case "numeric":
                if (reader.TokenType == JsonTokenType.Number)
                {
                    return reader.TryGetDecimal(out _);
                }
                return System.Buffers.Text.Utf8Parser.TryParse(reader.ValueSpan, out decimal _, out _);

            default:
                return true;
        }
    }

    private class TrieNode
    {
        public readonly TrieNode?[] Children = new TrieNode?[256];
        public ColumnSchema? Schema;
    }
}
