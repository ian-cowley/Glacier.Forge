using System;
using System.Threading.Tasks;

namespace Glacier.Forge;

/// <summary>
/// Distinguishes JSON token semantics so literal strings such as "", "null", "true", or "false"
/// are never confused with SQL NULL or boolean tokens.
/// </summary>
public enum FieldValueKind : byte
{
    Auto = 0,
    Null = 1,
    BooleanTrue = 2,
    BooleanFalse = 3,
    ScalarOrString = 4,
    RawJson = 5
}

public interface IRelationalSink : IAsyncDisposable
{
    Task InitializeAsync(string connectionString, string targetTable, ColumnSchema[] schema);

    /// <summary>
    /// Begins a new database record.
    /// </summary>
    void StartRecord();

    /// <summary>
    /// Writes a field value to the active record.
    /// </summary>
    /// <param name="colIndex">The 0-based column index as mapped in the database schema.</param>
    /// <param name="value">The UTF-8 bytes representing the value.</param>
    /// <param name="line">The complete JSON line context containing this value.</param>
    /// <param name="kind">Explicit JSON token semantics for the value.</param>
    void WriteField(int colIndex, ReadOnlySpan<byte> value, ReadOnlySpan<byte> line, FieldValueKind kind = FieldValueKind.Auto);

    /// <summary>
    /// Finalizes the active record, either committing it to the in-memory batch or discarding it.
    /// </summary>
    /// <param name="commit">True to commit the record to the batch; false to discard it.</param>
    /// <param name="line">The complete JSON line context.</param>
    void EndRecord(bool commit, ReadOnlySpan<byte> line);

    /// <summary>
    /// Asynchronously flushes all buffered records in the current batch to the database.
    /// </summary>
    Task FlushBatchAsync();
}
