using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Glacier.Forge;

public sealed class SqlServerSink : IRelationalSink
{
    private SqlConnection? _connection;
    private ColumnSchema[] _schema = null!;
    private string _targetTable = null!;
    private PooledBatchDataReader _dataReader = null!;
    
    private const int MaxBatchSize = 10000;
    private int _currentRecordIndex = 0;
    private bool _isRecordActive = false;

    // Per-record field buffering offsets to handle out-of-order and missing JSON fields
    private int[] _fieldOffsets = null!;
    private int[] _fieldLengths = null!;
    private bool[] _fieldPresent = null!;

    public async Task InitializeAsync(string connectionString, string targetTable, ColumnSchema[] schema)
    {
        _schema = schema;
        _targetTable = targetTable;
        _dataReader = new PooledBatchDataReader(schema, MaxBatchSize);
        
        _fieldOffsets = new int[schema.Length];
        _fieldLengths = new int[schema.Length];
        _fieldPresent = new bool[schema.Length];

        _connection = new SqlConnection(connectionString);
        await _connection.OpenAsync();
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
                _dataReader.SetNull(i, _currentRecordIndex);
                continue;
            }

            ReadOnlySpan<byte> valSpan = line.Slice(_fieldOffsets[i], _fieldLengths[i]);

            if (valSpan.IsEmpty || 
                valSpan.SequenceEqual("null"u8))
            {
                _dataReader.SetNull(i, _currentRecordIndex);
                continue;
            }

            string type = _schema[i].DataTypeLower;
            switch (type)
            {
                case "int":
                case "integer":
                case "int4":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out int intVal, out _))
                        _dataReader.SetValue(i, _currentRecordIndex, intVal);
                    else
                        _dataReader.SetNull(i, _currentRecordIndex);
                    break;

                case "bigint":
                case "int8":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out long longVal, out _))
                        _dataReader.SetValue(i, _currentRecordIndex, longVal);
                    else
                        _dataReader.SetNull(i, _currentRecordIndex);
                    break;

                case "boolean":
                case "bool":
                case "bit":
                    if (valSpan.SequenceEqual("true"u8) || valSpan.SequenceEqual("1"u8))
                    {
                        _dataReader.SetValue(i, _currentRecordIndex, true);
                    }
                    else if (valSpan.SequenceEqual("false"u8) || valSpan.SequenceEqual("0"u8))
                    {
                        _dataReader.SetValue(i, _currentRecordIndex, false);
                    }
                    else if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out bool boolVal, out _))
                    {
                        _dataReader.SetValue(i, _currentRecordIndex, boolVal);
                    }
                    else
                    {
                        _dataReader.SetNull(i, _currentRecordIndex);
                    }
                    break;

                case "datetime":
                case "datetime2":
                case "timestamp":
                case "timestamp without time zone":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out DateTime dtVal, out _, 'O'))
                    {
                        _dataReader.SetValue(i, _currentRecordIndex, dtVal);
                    }
                    else if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out DateTime dtVal2, out _))
                    {
                        _dataReader.SetValue(i, _currentRecordIndex, dtVal2);
                    }
                    else
                    {
                        try
                        {
                            string dateStr = System.Text.Encoding.UTF8.GetString(valSpan);
                            if (DateTime.TryParse(dateStr, out DateTime dtFallback))
                                _dataReader.SetValue(i, _currentRecordIndex, dtFallback);
                            else
                                _dataReader.SetNull(i, _currentRecordIndex);
                        }
                        catch
                        {
                            _dataReader.SetNull(i, _currentRecordIndex);
                        }
                    }
                    break;

                case "float":
                case "double precision":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out double dblVal, out _))
                        _dataReader.SetValue(i, _currentRecordIndex, dblVal);
                    else
                        _dataReader.SetNull(i, _currentRecordIndex);
                    break;

                case "decimal":
                case "numeric":
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out decimal decVal, out _))
                        _dataReader.SetValue(i, _currentRecordIndex, decVal);
                    else
                        _dataReader.SetNull(i, _currentRecordIndex);
                    break;

                default: // varchar, nvarchar, text
                    _dataReader.SetString(i, _currentRecordIndex, valSpan);
                    break;
            }
        }

        _currentRecordIndex++;
    }

    public async Task FlushBatchAsync()
    {
        if (_currentRecordIndex == 0)
            return;

        if (_connection == null)
            throw new InvalidOperationException("Sink is not initialized.");

        _dataReader.Reset(_currentRecordIndex);

        using SqlBulkCopy bulkCopy = new(_connection);
        bulkCopy.DestinationTableName = _targetTable;
            bulkCopy.BatchSize = _currentRecordIndex;
            
            // Map columns
            for (int i = 0; i < _schema.Length; i++)
            {
                bulkCopy.ColumnMappings.Add(i, _schema[i].ColumnName);
            }

            await bulkCopy.WriteToServerAsync(_dataReader);

        _currentRecordIndex = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentRecordIndex > 0)
        {
            await FlushBatchAsync();
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}

public sealed class PooledBatchDataReader : DbDataReader
{
    private readonly ColumnSchema[] _schema;
    private int _rowCount;
    private int _currentRow = -1;

    private readonly Array[] _columnBuffers;
    private readonly bool[][] _nullIndicators;

    private readonly char[] _stringBuffer;
    private int _stringBufferOffset;
    private readonly int[][] _stringOffsets;
    private readonly int[][] _stringLengths;

    // Zero-allocation cached SqlChars wrappers per column
    private readonly System.Data.SqlTypes.SqlChars[] _sqlCharsWrappers;

    public PooledBatchDataReader(ColumnSchema[] schema, int maxBatchSize)
    {
        _schema = schema;
        _columnBuffers = new Array[schema.Length];
        _nullIndicators = new bool[schema.Length][];
        _stringOffsets = new int[schema.Length][];
        _stringLengths = new int[schema.Length][];
        _sqlCharsWrappers = new System.Data.SqlTypes.SqlChars[schema.Length];

        // Size the string buffer generously (e.g. 10,000 rows * 512 chars/row average)
        _stringBuffer = new char[maxBatchSize * 512];

        for (int i = 0; i < schema.Length; i++)
        {
            _nullIndicators[i] = new bool[maxBatchSize];
            string type = schema[i].DataType.ToLowerInvariant();
            
            _columnBuffers[i] = type switch
            {
                "int" or "integer" or "int4" => new int[maxBatchSize],
                "bigint" or "int8" => new long[maxBatchSize],
                "boolean" or "bool" or "bit" => new bool[maxBatchSize],
                "datetime" or "datetime2" or "timestamp" or "timestamp without time zone" => new DateTime[maxBatchSize],
                "float" or "double precision" => new double[maxBatchSize],
                "decimal" or "numeric" => new decimal[maxBatchSize],
                _ => Array.Empty<string>() // empty placeholder for string types
            };

            if (_columnBuffers[i].Length == 0)
            {
                _stringOffsets[i] = new int[maxBatchSize];
                _stringLengths[i] = new int[maxBatchSize];

                // Pre-allocate SqlChars with a sensible initial size.
                int initialBufSize = schema[i].MaxLength > 0 ? schema[i].MaxLength : 2048;
                _sqlCharsWrappers[i] = new System.Data.SqlTypes.SqlChars(new char[initialBufSize]);
            }
        }
    }

    public void Reset(int rowCount)
    {
        _rowCount = rowCount;
        _currentRow = -1;
        _stringBufferOffset = 0;
    }

    public void SetNull(int colIndex, int rowIndex)
    {
        _nullIndicators[colIndex][rowIndex] = true;
    }

    public void SetValue<T>(int colIndex, int rowIndex, T value) where T : struct
    {
        _nullIndicators[colIndex][rowIndex] = false;
        ((T[])_columnBuffers[colIndex])[rowIndex] = value;
    }

    public void SetString(int colIndex, int rowIndex, ReadOnlySpan<byte> value)
    {
        _nullIndicators[colIndex][rowIndex] = false;

        int charCount = System.Text.Encoding.UTF8.GetCharCount(value);
        if (_stringBufferOffset + charCount > _stringBuffer.Length)
        {
            throw new InvalidOperationException("PooledBatchDataReader string buffer overflow.");
        }

        int written = System.Text.Encoding.UTF8.GetChars(value, _stringBuffer.AsSpan(_stringBufferOffset));
        _stringOffsets[colIndex][rowIndex] = _stringBufferOffset;
        _stringLengths[colIndex][rowIndex] = written;
        _stringBufferOffset += written;
    }

    // DbDataReader Implementation:
    public override bool Read()
    {
        _currentRow++;
        return _currentRow < _rowCount;
    }

    public override bool IsDBNull(int ordinal) => _nullIndicators[ordinal][_currentRow];

    public override int GetInt32(int ordinal) => ((int[])_columnBuffers[ordinal])[_currentRow];
    public override long GetInt64(int ordinal) => ((long[])_columnBuffers[ordinal])[_currentRow];
    public override bool GetBoolean(int ordinal) => ((bool[])_columnBuffers[ordinal])[_currentRow];
    public override DateTime GetDateTime(int ordinal) => ((DateTime[])_columnBuffers[ordinal])[_currentRow];
    public override double GetDouble(int ordinal) => ((double[])_columnBuffers[ordinal])[_currentRow];
    public override decimal GetDecimal(int ordinal) => ((decimal[])_columnBuffers[ordinal])[_currentRow];

    public override string GetString(int ordinal)
    {
        int offset = _stringOffsets[ordinal][_currentRow];
        int length = _stringLengths[ordinal][_currentRow];
        return new string(_stringBuffer, offset, length);
    }

    public override object GetValue(int ordinal)
    {
        if (IsDBNull(ordinal)) 
            return DBNull.Value;

        string type = _schema[ordinal].DataType.ToLowerInvariant();
        return type switch
        {
            "int" or "integer" or "int4" => GetInt32(ordinal),
            "bigint" or "int8" => GetInt64(ordinal),
            "boolean" or "bool" or "bit" => GetBoolean(ordinal),
            "datetime" or "datetime2" or "timestamp" or "timestamp without time zone" => GetDateTime(ordinal),
            "float" or "double precision" => GetDouble(ordinal),
            "decimal" or "numeric" => GetDecimal(ordinal),
            _ => GetSqlChars(ordinal) // Return reusable SqlChars wrapper to avoid string allocations
        };
    }

    private System.Data.SqlTypes.SqlChars GetSqlChars(int ordinal)
    {
        int offset = _stringOffsets[ordinal][_currentRow];
        int length = _stringLengths[ordinal][_currentRow];
        
        var wrapper = _sqlCharsWrappers[ordinal];
        
        if (wrapper.MaxLength < length)
        {
            wrapper = new System.Data.SqlTypes.SqlChars(new char[length * 2]);
            _sqlCharsWrappers[ordinal] = wrapper;
        }

        wrapper.Write(0, _stringBuffer, offset, length);
        wrapper.SetLength(length);

        return wrapper;
    }

    public override int FieldCount => _schema.Length;
    public override int Depth => 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool HasRows => _rowCount > 0;

    public override string GetName(int ordinal) => _schema[ordinal].ColumnName;
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _schema.Length; i++)
        {
            if (_schema[i].ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public override string GetDataTypeName(int ordinal) => _schema[ordinal].DataType;

    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        string type = _schema[ordinal].DataType.ToLowerInvariant();
        return type switch
        {
            "int" or "integer" or "int4" => typeof(int),
            "bigint" or "int8" => typeof(long),
            "boolean" or "bool" or "bit" => typeof(bool),
            "datetime" or "datetime2" or "timestamp" or "timestamp without time zone" => typeof(DateTime),
            "float" or "double precision" => typeof(double),
            "decimal" or "numeric" => typeof(decimal),
            _ => typeof(string)
        };
    }

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    // Required Abstract Overrides from DbDataReader / IDataRecord:
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool NextResult() => false;
    public override void Close() { }

    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
}
