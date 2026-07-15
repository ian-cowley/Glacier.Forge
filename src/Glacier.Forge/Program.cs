using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Glacier.Forge;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Parse Command Line Arguments
        string? dbTypeInput = null;
        string? connString = null;
        string? tableName = null;
        string? licenseKey = null;
        string? inputFilePath = null;
        string? customMapInput = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-db" when i + 1 < args.Length:
                    dbTypeInput = args[++i];
                    break;
                case "-c" when i + 1 < args.Length:
                    connString = args[++i];
                    break;
                case "-t" when i + 1 < args.Length:
                    tableName = args[++i];
                    break;
                case "-k" when i + 1 < args.Length:
                    licenseKey = args[++i];
                    break;
                case "-f" when i + 1 < args.Length:
                case "--file" when i + 1 < args.Length:
                    inputFilePath = args[++i];
                    break;
                case "-m" when i + 1 < args.Length:
                case "--map" when i + 1 < args.Length:
                    customMapInput = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Console.Error.WriteLine("Error: Missing license key (-k).");
            return 1;
        }

        // --- THE LICENSE GATE ---
        if (!LicenseManager.ValidateLicense(licenseKey))
        {
            return 1; // Exit immediately if validation fails
        }

        if (string.IsNullOrWhiteSpace(dbTypeInput) || string.IsNullOrWhiteSpace(tableName))
        {
            Console.Error.WriteLine("Error: Missing required database arguments.");
            Console.Error.WriteLine("Usage: dotnet run -- -k \"license_key\" -db [sqlserver|postgres|pg] [-c \"connection_string\"] -t \"table_name\"");
            return 1;
        }

        DatabaseType dbType;
        if (dbTypeInput.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            dbType = DatabaseType.SqlServer;
        }
        else if (dbTypeInput.Equals("postgres", StringComparison.OrdinalIgnoreCase) || 
                 dbTypeInput.Equals("pg", StringComparison.OrdinalIgnoreCase))
        {
            dbType = DatabaseType.Postgres;
        }
        else
        {
            Console.Error.WriteLine($"Error: Unsupported database type '{dbTypeInput}'.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(connString))
        {
            connString = GetConnectionStringFromConfig(dbType);
            if (string.IsNullOrWhiteSpace(connString))
            {
                Console.Error.WriteLine("Error: Connection string not provided via '-c' and not found in appsettings.json.");
                return 1;
            }
        }

        Console.WriteLine($"Starting ingestion engine...");
        Console.WriteLine($"Database Type: {dbType}");
        Console.WriteLine($"Target Table:  {tableName}");

        try
        {
            // 1. Fetch Database Schema Columns
            Console.WriteLine("Fetching database schema...");
            ColumnSchema[] columns = await FetchSchemaAsync(dbType, connString, tableName);
            if (columns.Length == 0)
            {
                Console.Error.WriteLine($"Error: No columns found for table '{tableName}'. Ensure table exists and connection string is correct.");
                return 1;
            }

            Console.WriteLine($"Discovered {columns.Length} columns:");
            foreach (var col in columns)
            {
                Console.WriteLine($"  [{col.Index}] {col.ColumnName} ({col.DataType}, Nullable={col.IsNullable}, MaxLength={col.MaxLength})");
            }

            // 2. Parse Custom Mappings
            var customMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(customMapInput))
            {
                var pairs = customMapInput.Split(',');
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(':');
                    if (parts.Length == 2)
                    {
                        customMappings[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            // 3. Load Schema into Validator
            var validator = new SchemaValidator();
            validator.LoadSchema(columns, customMappings);

            // 3. Initialize Sink
            IRelationalSink sink = dbType switch
            {
                DatabaseType.SqlServer => new SqlServerSink(),
                DatabaseType.Postgres => new PostgresSink(),
                _ => throw new InvalidOperationException()
            };

            await sink.InitializeAsync(connString, tableName, columns);

            // 4. Ingest from file or stdin
            if (!string.IsNullOrWhiteSpace(inputFilePath))
            {
                if (!System.IO.File.Exists(inputFilePath))
                {
                    Console.Error.WriteLine($"Error: Input file '{inputFilePath}' not found.");
                    await sink.DisposeAsync();
                    return 1;
                }

                Console.WriteLine($"Reading from file: {inputFilePath}...");
                await IngestFileAsync(inputFilePath, sink, validator, columns);
            }
            else
            {
                var stdinStream = Console.OpenStandardInput();
                var reader = PipeReader.Create(stdinStream);

                Console.WriteLine("Listening to stdin for JSONL streaming...");
                await IngestStreamAsync(reader, sink, validator, columns);
            }

            await sink.DisposeAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Execution failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task<ColumnSchema[]> FetchSchemaAsync(DatabaseType dbType, string connectionString, string tableName)
    {
        var columns = new List<ColumnSchema>();

        if (dbType == DatabaseType.SqlServer)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            string query = """
                SELECT 
                    c.name AS ColumnName,
                    t.name AS DataType,
                    c.is_nullable AS IsNullable,
                    CASE WHEN t.name IN ('varchar', 'nvarchar', 'char', 'nchar') THEN c.max_length ELSE -1 END AS MaxLength
                FROM sys.columns c
                JOIN sys.types t ON c.user_type_id = t.user_type_id
                WHERE c.object_id = OBJECT_ID(@TableName)
                ORDER BY c.column_id;
                """;

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await cmd.ExecuteReaderAsync();
            int index = 0;
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnSchema
                {
                    Index = index++,
                    ColumnName = reader.GetString(0),
                    DataType = reader.GetString(1),
                    IsNullable = reader.GetBoolean(2),
                    MaxLength = reader.GetInt32(3)
                });
            }
        }
        else if (dbType == DatabaseType.Postgres)
        {
            using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string schemaName = "public";
            string rawTableName = tableName;

            int dotIndex = tableName.IndexOf('.');
            if (dotIndex >= 0)
            {
                schemaName = tableName[..dotIndex];
                rawTableName = tableName[(dotIndex + 1)..];
            }

            string query = """
                SELECT 
                    column_name AS ColumnName,
                    data_type AS DataType,
                    CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                    COALESCE(character_maximum_length, -1) AS MaxLength
                FROM information_schema.columns
                WHERE table_name = @TableName AND table_schema = @TableSchema
                ORDER BY ordinal_position;
                """;

            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@TableName", rawTableName);
            cmd.Parameters.AddWithValue("@TableSchema", schemaName);

            using var reader = await cmd.ExecuteReaderAsync();
            int index = 0;
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnSchema
                {
                    Index = index++,
                    ColumnName = reader.GetString(0),
                    DataType = reader.GetString(1),
                    IsNullable = reader.GetInt32(2) == 1,
                    MaxLength = reader.GetInt32(3)
                });
            }
        }

        return [.. columns];
    }

    private static async Task IngestStreamAsync(PipeReader reader, IRelationalSink sink, SchemaValidator validator, ColumnSchema[] schema)
    {
        const int maxBatchSize = 10000;
        int processedCount = 0;
        int successCount = 0;
        int failCount = 0;

        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
            {
                processedCount++;

                bool success = ProcessLine(line, sink, validator, schema);
                if (success)
                    successCount++;
                else
                    failCount++;

                if (successCount > 0 && successCount % maxBatchSize == 0)
                {
                    await sink.FlushBatchAsync();
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                if (!buffer.IsEmpty)
                {
                    processedCount++;
                    bool success = ProcessLine(buffer, sink, validator, schema);
                    if (success)
                        successCount++;
                    else
                        failCount++;
                }
                break;
            }
        }

        await sink.FlushBatchAsync();
        Console.WriteLine($"Ingestion completed. Total parsed: {processedCount}, Imported: {successCount}, Rejected/Dropped (DLQ): {failCount}");
    }

    private static async Task IngestFileAsync(string filePath, IRelationalSink sink, SchemaValidator validator, ColumnSchema[] schema)
    {
        var fileInfo = new System.IO.FileInfo(filePath);
        int processedCount = 0;
        int successCount = 0;
        int failCount = 0;
        const int maxBatchSize = 10000;

        Exception? innerException = null;

        HybridIoDispatcher.ProcessFile(filePath, fileInfo.Length, data =>
        {
            try
            {
                ReadOnlySpan<byte> remaining = data;
                while (remaining.Length > 0)
                {
                    int nextNewline = remaining.IndexOf((byte)'\n');
                    ReadOnlySpan<byte> lineBytes;
                    if (nextNewline >= 0)
                    {
                        lineBytes = remaining[..nextNewline];
                        remaining = remaining[(nextNewline + 1)..];
                    }
                    else
                    {
                        lineBytes = remaining;
                        remaining = [];
                    }

                    if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
                    {
                        lineBytes = lineBytes[..^1];
                    }

                    if (lineBytes.IsEmpty) continue;

                    processedCount++;

                    bool success = ProcessRecord(lineBytes, sink, validator, schema);
                    if (success)
                    {
                        successCount++;
                        if (successCount % maxBatchSize == 0)
                        {
                            sink.FlushBatchAsync().GetAwaiter().GetResult();
                        }
                    }
                    else
                    {
                        failCount++;
                        try
                        {
                            Console.Error.WriteLine(System.Text.Encoding.UTF8.GetString(lineBytes));
                        }
                        catch
                        {
                            Console.Error.WriteLine("Invalid UTF-8 line rejected");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                innerException = ex;
            }
        });

        if (innerException != null)
        {
            throw innerException;
        }

        await sink.FlushBatchAsync();
        Console.WriteLine($"Ingestion completed. Total parsed: {processedCount}, Imported: {successCount}, Rejected/Dropped (DLQ): {failCount}");
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = buffer.PositionOf((byte)'\n');
        if (position == null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static bool ProcessLine(ReadOnlySequence<byte> line, IRelationalSink sink, SchemaValidator validator, ColumnSchema[] schema)
    {
        int byteLength = (int)line.Length;
        if (byteLength == 0) return false;

        byte[]? rentedBytes = null;
        Span<byte> byteSpan = byteLength <= 2048 
            ? stackalloc byte[2048] 
            : (rentedBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(byteLength));

        try
        {
            line.CopyTo(byteSpan);
            Span<byte> lineBytes = byteSpan[..byteLength];
            
            if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
            {
                lineBytes = lineBytes[..^1];
            }

            if (lineBytes.IsEmpty) return false;

            bool success = ProcessRecord(lineBytes, sink, validator, schema);
            if (!success)
            {
                try
                {
                    Console.Error.WriteLine(System.Text.Encoding.UTF8.GetString(lineBytes));
                }
                catch
                {
                    Console.Error.WriteLine("Invalid UTF-8 line rejected");
                }
            }
            return success;
        }
        finally
        {
            if (rentedBytes != null)
                System.Buffers.ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }

    public static bool ProcessRecord(ReadOnlySpan<byte> byteLine, IRelationalSink sink, SchemaValidator validator, ColumnSchema[] schema)
    {
        sink.StartRecord();

        bool recordValid = true;
        Span<bool> presentFields = stackalloc bool[schema.Length];
        presentFields.Clear();

        try
        {
            var reader = new System.Text.Json.Utf8JsonReader(byteLine);

            if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                sink.EndRecord(false, byteLine);
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
                {
                    ReadOnlySpan<byte> propertyName = reader.ValueSpan;

                    if (validator.TryMatchColumn(propertyName, out var colSchema))
                    {
                        if (!reader.Read())
                        {
                            recordValid = false;
                            break;
                        }

                        if (SchemaValidator.IsValidFormat(colSchema, ref reader))
                        {
                            ReadOnlySpan<byte> propertyValue;
                            if (reader.TokenType == System.Text.Json.JsonTokenType.True)
                            {
                                propertyValue = "true"u8;
                            }
                            else if (reader.TokenType == System.Text.Json.JsonTokenType.False)
                            {
                                propertyValue = "false"u8;
                            }
                            else if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
                            {
                                propertyValue = "null"u8;
                            }
                            else
                            {
                                propertyValue = reader.ValueSpan;
                            }

                            sink.WriteField(colSchema.Index, propertyValue, byteLine);
                            presentFields[colSchema.Index] = true;
                        }
                        else
                        {
                            recordValid = false;
                            break;
                        }
                    }
                    else
                    {
                        if (!reader.Read())
                        {
                            recordValid = false;
                            break;
                        }

                        if (reader.TokenType == System.Text.Json.JsonTokenType.StartObject || 
                            reader.TokenType == System.Text.Json.JsonTokenType.StartArray)
                        {
                            reader.Skip();
                        }
                    }
                }
            }
        }
        catch
        {
            recordValid = false;
        }

        if (recordValid)
        {
            for (int i = 0; i < schema.Length; i++)
            {
                if (!presentFields[i] && !schema[i].IsNullable)
                {
                    recordValid = false;
                    break;
                }
            }
        }

        sink.EndRecord(recordValid, byteLine);
        return recordValid;
    }

    private static string? GetConnectionStringFromConfig(DatabaseType dbType)
    {
        string? connStr = null;

        // 1. Try reading from appsettings.json
        try
        {
            string configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (System.IO.File.Exists(configPath))
            {
                byte[] jsonBytes = System.IO.File.ReadAllBytes(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize(
                    jsonBytes,
                    LicenseJsonContext.Default.AppConfig);

                if (config?.ConnectionStrings != null)
                {
                    connStr = dbType switch
                    {
                        DatabaseType.SqlServer => config.ConnectionStrings.SqlServer,
                        DatabaseType.Postgres => config.ConnectionStrings.Postgres,
                        _ => null
                    };
                }
            }
        }
        catch { }

        // 2. Fall back to User Secrets if placeholder or missing
        if (string.IsNullOrWhiteSpace(connStr) || connStr.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string userSecretsFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft",
                    "UserSecrets",
                    "f34b054f-c873-4b64-9d11-bb0b3f033a4a");
                string secretsPath = System.IO.Path.Combine(userSecretsFolder, "secrets.json");

                if (System.IO.File.Exists(secretsPath))
                {
                    byte[] secretsBytes = System.IO.File.ReadAllBytes(secretsPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize(
                        secretsBytes,
                        LicenseJsonContext.Default.AppConfig);

                    if (config?.ConnectionStrings != null)
                    {
                        string? secretConn = dbType switch
                        {
                            DatabaseType.SqlServer => config.ConnectionStrings.SqlServer,
                            DatabaseType.Postgres => config.ConnectionStrings.Postgres,
                            _ => null
                        };
                        if (!string.IsNullOrWhiteSpace(secretConn))
                        {
                            connStr = secretConn;
                        }
                    }
                }
            }
            catch { }
        }

        return connStr;
    }
}
