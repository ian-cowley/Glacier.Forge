using System;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Glacier.Forge;

public sealed class PostgresSink : IRelationalSink
{
    private string _connectionString = null!;
    private ColumnSchema[] _schema = null!;
    private string _targetTable = null!;
    private string _copySql = null!;
    
    private const int MaxBatchSize = 10000;
    private int _currentRecordIndex = 0;
    private bool _isRecordActive = false;

    // Local in-memory batch buffers to prevent holding connection/transactions open during slow ingestion
    private Array[] _columnBuffers = null!;
    private bool[][] _nullIndicators = null!;

    // Buffer for all string characters (stored as raw UTF-8 bytes) in the current batch
    private byte[] _stringBuffer = null!;
    private int _stringBufferOffset = 0;
    private int[][] _stringOffsets = null!;
    private int[][] _stringLengths = null!;

    // Per-record field buffering offsets to handle out-of-order and missing JSON fields
    private int[] _fieldOffsets = null!;
    private int[] _fieldLengths = null!;
    private bool[] _fieldPresent = null!;

    public Task InitializeAsync(string connectionString, string targetTable, ColumnSchema[] schema)
    {
        _connectionString = connectionString;
        _schema = schema;
        _targetTable = targetTable;

        _fieldOffsets = new int[schema.Length];
        _fieldLengths = new int[schema.Length];
        _fieldPresent = new bool[schema.Length];

        _columnBuffers = new Array[schema.Length];
        _nullIndicators = new bool[schema.Length][];
        _stringOffsets = new int[schema.Length][];
        _stringLengths = new int[schema.Length][];

        // 10,000 records * 512 bytes average size
        _stringBuffer = new byte[MaxBatchSize * 512];

        for (int i = 0; i < schema.Length; i++)
        {
            _nullIndicators[i] = new bool[MaxBatchSize];
            string type = schema[i].DataType.ToLowerInvariant();
            
            _columnBuffers[i] = type switch
            {
                "int" or "integer" or "int4" => new int[MaxBatchSize],
                "bigint" or "int8" => new long[MaxBatchSize],
                "boolean" or "bool" or "bit" => new bool[MaxBatchSize],
                "datetime" or "datetime2" or "timestamp" or "timestamp without time zone" or "timestamp with time zone" or "timestamptz" => new DateTime[MaxBatchSize],
                "float" or "double precision" => new double[MaxBatchSize],
                "decimal" or "numeric" => new decimal[MaxBatchSize],
                _ => Array.Empty<byte>() // empty placeholder for strings
            };

            if (_columnBuffers[i].Length == 0)
            {
                _stringOffsets[i] = new int[MaxBatchSize];
                _stringLengths[i] = new int[MaxBatchSize];
            }
        }

        // Build COPY command: COPY tableName (col1, col2, ...) FROM STDIN (FORMAT BINARY)
        var columnsBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < schema.Length; i++)
        {
            if (i > 0) columnsBuilder.Append(", ");
            columnsBuilder.Append('"').Append(schema[i].ColumnName).Append('"');
        }

        _copySql = $"COPY \"{targetTable}\" ({columnsBuilder}) FROM STDIN (FORMAT BINARY)";

        return Task.CompletedTask;
    }

    public void StartRecord()
    {
        Array.Clear(_fieldPresent, 0, _fieldPresent.Length);
        _isRecordActive = true;
    }

    public void WriteField(int colIndex, ReadOnlySpan<byte> value, ReadOnlySpan<byte> line)
    {
        if (!_isRecordActive)
            throw new InvalidOperationException("No active record. Call StartRecord first.");

        unsafe
        {
            fixed (byte* pLine = line)
            fixed (byte* pVal = value)
            {
                long diff = pVal - pLine;
                _fieldOffsets[colIndex] = (int)diff;
                _fieldLengths[colIndex] = value.Length;
                _fieldPresent[colIndex] = true;
            }
        }
    }

    public void EndRecord(bool commit, ReadOnlySpan<byte> line)
    {
        if (!_isRecordActive)
            throw new InvalidOperationException("No active record to end.");

        _isRecordActive = false;

        if (!commit)
            return;

        // Process buffered fields for the current row in schema order
        for (int i = 0; i < _schema.Length; i++)
        {
            if (!_fieldPresent[i])
            {
                _nullIndicators[i][_currentRecordIndex] = true;
                continue;
            }

            ReadOnlySpan<byte> valSpan = line.Slice(_fieldOffsets[i], _fieldLengths[i]);

            if (valSpan.IsEmpty || 
                valSpan.SequenceEqual("null"u8))
            {
                _nullIndicators[i][_currentRecordIndex] = true;
                continue;
            }

            _nullIndicators[i][_currentRecordIndex] = false;

            string type = _schema[i].DataTypeLower;
            switch (type)
            {
                case "int":
                case "integer":
                case "int4":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out int intVal, out _))
                    {
                        ((int[])_columnBuffers[i])[_currentRecordIndex] = intVal;
                    }
                    else
                    {
                        _nullIndicators[i][_currentRecordIndex] = true;
                    }
                    break;

                case "bigint":
                case "int8":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out long longVal, out _))
                    {
                        ((long[])_columnBuffers[i])[_currentRecordIndex] = longVal;
                    }
                    else
                    {
                        _nullIndicators[i][_currentRecordIndex] = true;
                    }
                    break;

                case "boolean":
                case "bool":
                case "bit":
                    if (valSpan.SequenceEqual("true"u8) || valSpan.SequenceEqual("1"u8))
                    {
                        ((bool[])_columnBuffers[i])[_currentRecordIndex] = true;
                    }
                    else if (valSpan.SequenceEqual("false"u8) || valSpan.SequenceEqual("0"u8))
                    {
                        ((bool[])_columnBuffers[i])[_currentRecordIndex] = false;
                    }
                    else if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out bool boolVal, out _))
                    {
                        ((bool[])_columnBuffers[i])[_currentRecordIndex] = boolVal;
                    }
                    else
                    {
                        _nullIndicators[i][_currentRecordIndex] = true;
                    }
                    break;

                case "datetime":
                case "datetime2":
                case "timestamp":
                case "timestamp without time zone":
                case "timestamp with time zone":
                case "timestamptz":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out DateTime dtVal, out _, 'O'))
                    {
                        ((DateTime[])_columnBuffers[i])[_currentRecordIndex] = dtVal;
                    }
                    else if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out DateTime dtVal2, out _))
                    {
                        ((DateTime[])_columnBuffers[i])[_currentRecordIndex] = dtVal2;
                    }
                    else
                    {
                        try
                        {
                            string dateStr = System.Text.Encoding.UTF8.GetString(valSpan);
                            if (DateTime.TryParse(dateStr, out DateTime dtFallback))
                                ((DateTime[])_columnBuffers[i])[_currentRecordIndex] = dtFallback;
                            else
                                _nullIndicators[i][_currentRecordIndex] = true;
                        }
                        catch
                        {
                            _nullIndicators[i][_currentRecordIndex] = true;
                        }
                    }
                    break;

                case "float":
                case "double precision":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out double dblVal, out _))
                    {
                        ((double[])_columnBuffers[i])[_currentRecordIndex] = dblVal;
                    }
                    else
                    {
                        _nullIndicators[i][_currentRecordIndex] = true;
                    }
                    break;

                case "decimal":
                case "numeric":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out decimal decVal, out _))
                    {
                        ((decimal[])_columnBuffers[i])[_currentRecordIndex] = decVal;
                    }
                    else
                    {
                        _nullIndicators[i][_currentRecordIndex] = true;
                    }
                    break;

                default: // varchar, nvarchar, text, character varying
                    if (_stringBufferOffset + valSpan.Length > _stringBuffer.Length)
                    {
                        throw new InvalidOperationException("PostgresSink string buffer overflow.");
                    }
                    valSpan.CopyTo(_stringBuffer.AsSpan(_stringBufferOffset));
                    _stringOffsets[i][_currentRecordIndex] = _stringBufferOffset;
                    _stringLengths[i][_currentRecordIndex] = valSpan.Length;
                    _stringBufferOffset += valSpan.Length;
                    break;
            }
        }

        _currentRecordIndex++;
    }

    public async Task FlushBatchAsync()
    {
        if (_currentRecordIndex == 0)
            return;

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        using (var importer = await connection.BeginBinaryImportAsync(_copySql))
        {
            for (int rowIndex = 0; rowIndex < _currentRecordIndex; rowIndex++)
            {
                importer.StartRow();

                for (int colIndex = 0; colIndex < _schema.Length; colIndex++)
                {
                    if (_nullIndicators[colIndex][rowIndex])
                    {
                        importer.WriteNull();
                        continue;
                    }

                    string type = _schema[colIndex].DataTypeLower;
                    NpgsqlDbType npgsqlType = MapToNpgsqlDbType(type);

                    switch (type)
                    {
                        case "int":
                        case "integer":
                        case "int4":
                            importer.Write(((int[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        case "bigint":
                        case "int8":
                            importer.Write(((long[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        case "boolean":
                        case "bool":
                        case "bit":
                            importer.Write(((bool[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        case "datetime":
                        case "datetime2":
                        case "timestamp":
                        case "timestamp without time zone":
                        case "timestamp with time zone":
                        case "timestamptz":
                            importer.Write(((DateTime[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        case "float":
                        case "double precision":
                            importer.Write(((double[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        case "decimal":
                        case "numeric":
                            importer.Write(((decimal[])_columnBuffers[colIndex])[rowIndex], npgsqlType);
                            break;

                        default: // varchar, nvarchar, text, character varying
                            int offset = _stringOffsets[colIndex][rowIndex];
                            int length = _stringLengths[colIndex][rowIndex];
                            // Write directly as ReadOnlyMemory<byte> containing raw UTF-8 bytes
                            importer.Write(_stringBuffer.AsMemory(offset, length), npgsqlType);
                            break;
                    }
                }
            }

            await importer.CompleteAsync();
        }

        // Reset buffer cursors
        _currentRecordIndex = 0;
        _stringBufferOffset = 0;
    }

    private static NpgsqlDbType MapToNpgsqlDbType(string type)
    {
        return type switch
        {
            "int" or "integer" or "int4" => NpgsqlDbType.Integer,
            "bigint" or "int8" => NpgsqlDbType.Bigint,
            "boolean" or "bool" or "bit" => NpgsqlDbType.Boolean,
            "datetime" or "datetime2" or "timestamp" or "timestamp without time zone" => NpgsqlDbType.Timestamp,
            "timestamp with time zone" or "timestamptz" => NpgsqlDbType.TimestampTz,
            "float" or "double precision" => NpgsqlDbType.Double,
            "decimal" or "numeric" => NpgsqlDbType.Numeric,
            _ => NpgsqlDbType.Text
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentRecordIndex > 0)
        {
            try
            {
                await FlushBatchAsync();
            }
            catch
            {
                // Suppress errors during final flush on dispose
            }
        }
    }
}
