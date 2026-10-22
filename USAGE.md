# Glacier.Forge Operational and Reference Manual

**Product**: Glacier.Forge — High-Performance Semantic-to-Relational Hardener  
**Standard Compliance**: Diátaxis Documentation Framework (How-To Guides & Reference)  
**Linguistic Standard**: ASD-STE100 Simplified Technical English  
**Runtime Target**: .NET 10.0 (C# 13, Native AOT Compatible)  
**Document Revision**: 2.0.0  

---

## 1. Overview and Prerequisites

Glacier.Forge is a high-speed command-line tool. It ingests streaming JSONL data, validates the data against a relational database schema, and bulk-loads the validated records directly into PostgreSQL or Microsoft SQL Server.

### 1.1 System Requirements

- **Operating System**:
  - Microsoft Windows 10/11 or Windows Server 2019+ (x64, ARM64)
  - Linux (Ubuntu 22.04+, Debian 12+, RHEL 9+, Alpine 3.19+ with glibc/musl)
  - macOS 13+ (Apple Silicon ARM64, Intel x64)
- **Runtime Environment**:
  - .NET 10.0 SDK / Runtime (for managed execution), or
  - Zero runtime dependencies when executing compiled Native AOT binaries.
- **Target Relational Databases**:
  - Microsoft SQL Server 2016 or newer (including Azure SQL Database)
  - PostgreSQL 12 or newer (including AWS Aurora PostgreSQL and Supabase)

---

## 2. Command-Line Interface Reference

### 2.1 Command Syntax

Execute the Glacier.Forge binary using the following syntax:

```bash
Glacier.Forge --db <sqlserver|postgres> --table <schema.table_name> [options]
```

### 2.2 CLI Arguments Table

| Flag | Shorthand | Required | Allowed Values | Description |
| :--- | :--- | :--- | :--- | :--- |
| `--db` | `-db` | **Yes** | `sqlserver`, `postgres`, `pg` | Specifies the target relational database engine. |
| `--table` | `-t` | **Yes** | String (e.g., `public.events`, `dbo.users`) | Specifies the destination table name. You can include a schema prefix. |
| `--conn` | `-c`, `--connection` | No | ADO.NET Connection String | Database connection string. If omitted, the engine reads configuration providers. |
| `--file` | `-f` | No | File system path | Local path to a `.jsonl` file. If omitted, the engine reads from standard input (`stdin`). |
| `--map` | `-m` | No | Comma-separated pairs | Dynamic key-to-column mappings (e.g., `jsonKey:dbColumn`). |
| `--key` | `-k` | No | Base64 token string | Optional ECDSA P-256 enterprise digital license key. |
| `--help` | `-h`, `-?` | No | None | Prints CLI parameter instructions to the terminal. |

### 2.3 Environment Variable Overrides

If you run Glacier.Forge in containerized or headless environments, you can supply parameters through environment variables:

| Environment Variable | Equivalent CLI Argument | Purpose |
| :--- | :--- | :--- |
| `ConnectionStrings__SqlServer` | `--conn <string>` | Default connection string for Microsoft SQL Server targets. |
| `ConnectionStrings__Postgres` | `--conn <string>` | Default connection string for PostgreSQL targets. |
| `GLACIER_LICENSE` | `--key <string>` | Cryptographic license key for enterprise compliance verification. |

### 2.4 Process Exit Codes

Glacier.Forge returns standardized POSIX exit codes upon completion:

| Exit Code | Status | Meaning |
| :--- | :--- | :--- |
| `0` | **Success** | The ingestion batch completed with zero critical system faults. |
| `1` | **Execution Failure** | The process failed due to missing arguments, connection failure, or invalid licenses. |

---

## 3. Configuration and Dynamic Schema Discovery

Glacier.Forge requires zero static model code, JSON Schema files, or mapping configuration files. On startup, the engine queries the target database metadata catalog to construct an in-memory schema validator.

### 3.1 Database Catalog Discovery Queries

#### Microsoft SQL Server Discovery Query
The engine queries `sys.columns`, `sys.types`, and `sys.tables`:

```sql
SELECT 
    c.column_id - 1 AS [Index],
    c.name AS ColumnName,
    t.name AS DataType,
    c.is_nullable AS IsNullable,
    c.max_length AS MaxLength
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(@TableName)
ORDER BY c.column_id;
```

#### PostgreSQL Discovery Query
The engine queries `information_schema.columns`:

```sql
SELECT 
    ordinal_position - 1 AS Index,
    column_name AS ColumnName,
    data_type AS DataType,
    (is_nullable = 'YES') AS IsNullable,
    COALESCE(character_maximum_length, -1) AS MaxLength
FROM information_schema.columns
WHERE table_schema = @TableSchema AND table_name = @TableName
ORDER BY ordinal_position;
```

### 3.2 Supported Data Types and Validation Rules

The table below describes how raw UTF-8 byte spans are validated against database column types:

| Database Column Type | Supported JSON Token Types | Validation and Coercion Method |
| :--- | :--- | :--- |
| `int`, `integer`, `int4` | `Number` | Validates via `Utf8Parser.TryParse(span, out int)`. |
| `smallint`, `int2` | `Number` | Validates via `Utf8Parser.TryParse(span, out short)`. |
| `bigint`, `int8` | `Number` | Validates via `Utf8Parser.TryParse(span, out long)`. |
| `uuid`, `uniqueidentifier`, `guid` | `String` (UUID format) | Validates via `Utf8Parser.TryParse(span, out Guid)`. |
| `varchar`, `nvarchar`, `text` | `String` | Validates UTF-8 character length against `MaxLength`. |
| `datetime`, `datetime2`, `timestamp` | `String` (ISO 8601) | Validates via `Utf8Parser.TryParse(span, out DateTime)`. |
| `boolean`, `bool`, `bit` | `True`, `False`, literal numbers | Matches `"true"u8`, `"false"u8`, `"1"u8`, `"0"u8`. |
| `float`, `double precision` | `Number` | Validates via `Utf8Parser.TryParse(span, out double)`. |
| `decimal`, `numeric` | `Number` | Validates via `Utf8Parser.TryParse(span, out decimal)`. |
| `json`, `jsonb` | Any valid JSON token | Passes raw UTF-8 span through directly. |

### 3.3 Schema Mismatch Rules

- **Unordered Fields**: JSON properties can appear in any order. The 256-ary Trie matches column names independently of field order.
- **Superfluous Fields**: If the incoming JSON contains keys not present in the database table, the engine calls `reader.Skip()` to bypass the key and value without allocating memory.
- **Missing Nullable Fields**: If a database column is omitted from a JSON line, the engine records a `NULL` indicator for that column.
- **Missing Non-Nullable Fields**: If a database column is marked `NOT NULL` and does not appear in the JSON record, the engine rejects the record and writes it to `stderr`.
- **ASCII Case Insensitivity**: Keys are matched case-insensitively using an inlined bit-shift function (`byte - 32`). For example, `userid`, `userId`, and `USERID` all resolve to `UserId`.

---

## 4. Schema Key Mapping (`--map` / `-m`)

Use the `--map` parameter when incoming JSON keys differ from target database column names.

### 4.1 Syntax

Specify comma-separated pairs of `SourceJsonKey:TargetDatabaseColumn`:

```bash
--map "json_key_1:DbColumn1,json_key_2:DbColumn2"
```

### 4.2 End-to-End Mapping Example

Consider an incoming stream with the following JSONL format:

```json
{"user_ident": 1042, "user_display_name": "Alice Smith", "is_authorized": true, "timestamp_epoch": "2026-10-22T08:00:00Z"}
```

Target PostgreSQL table definition:

```sql
CREATE TABLE public.audit_log (
    id INT NOT NULL,
    username VARCHAR(100) NOT NULL,
    active BOOLEAN NOT NULL,
    created_at TIMESTAMP NOT NULL
);
```

Execute Glacier.Forge with the following mapping command:

```bash
Glacier.Forge --db postgres --table public.audit_log \
  --map "user_ident:id,user_display_name:username,is_authorized:active,timestamp_epoch:created_at" \
  --file stream.jsonl
```

The engine adds the aliases into the 256-ary Trie. When the parser encounters `"user_ident"u8`, it resolves directly to the schema definition for the `id` column with zero heap allocations.

---

## 5. Streaming Ingestion via Standard Input (`stdin`)

Piping data through standard input is the recommended deployment pattern for live streaming architectures (such as real-time LLM token generators).

### 5.1 Bash / Linux Pipeline Example

```bash
# Stream records directly from a Python producer into PostgreSQL
python -u stream_inference.py | Glacier.Forge --db postgres --table public.token_metrics
```

### 5.2 PowerShell / Windows Pipeline Example

```powershell
# Stream curl API output directly into Microsoft SQL Server
curl.exe -N https://api.internal/v1/stream | Glacier.Forge --db sqlserver --table dbo.api_events
```

### 5.3 Batch Buffering and Flush Thresholds

- **Batch Size Limit**: The engine buffers up to **10,000 records** in contiguous unmanaged arrays before triggering a database flush.
- **Stream EOF**: When the input stream closes (EOF), the engine automatically flushes any remaining buffered records.
- **Connection Safety**: For PostgreSQL targets, database connections and transactions are opened only during the batch flush. This prevents holding idle database connections open during slow stream periods.

---

## 6. Local File Ingestion & Zero-Copy Hybrid I/O

When you supply the `--file` argument, Glacier.Forge activates the `HybridIoDispatcher`. The dispatcher chooses the optimal I/O strategy based on file size.

### 6.1 Small File Processing (< 1 MB)

For files smaller than 1 MB, the dispatcher rents a buffer from `ArrayPool<byte>.Shared` and reads the file using OS-level `RandomAccess.Read` with `FileOptions.SequentialScan`:

```bash
Glacier.Forge --db postgres --table public.users --file sample_10k.jsonl
```

### 6.2 Large File Processing (>= 1 MB and Multi-Gigabyte Files)

For files of 1 MB or larger, the dispatcher creates a `MemoryMappedFile` and memory view accessor. It acquires a raw pointer (`byte*`) to access data pages directly without intermediate user-space buffer copies:

```bash
Glacier.Forge --db sqlserver --table dbo.historical_logs --file D:\dumps\large_export.jsonl
```

> **NOTE**: If a file exceeds 2 GB (`int.MaxValue` bytes), Glacier.Forge automatically divides the memory map into 1 GB blocks. It scans backward from the candidate boundary to locate the last newline character (`\n`), guaranteeing that records are never sliced in half.

---

## 7. Error Handling, Validation Failures, and Dead-Letter Queue (DLQ)

Glacier.Forge guarantees that malformed records do not corrupt database transactions or stop the ingestion process.

### 7.1 Record Rejection Criteria

A record is rejected if any of the following conditions occur:
1. **Malformed Syntax**: The line is not valid JSON or lacks enclosing curly braces `{}`.
2. **Type Coercion Failure**: A value cannot be parsed into the target column type (e.g., text inside an integer column).
3. **Nullability Violation**: A database column is marked `NOT NULL` and the record either omits the field or provides a `null` literal.
4. **Length Violation**: A string value exceeds the column's defined `MaxLength`.

### 7.2 Isolating Rejected Records via Dead-Letter Queue

When a record fails validation:
- The engine marks the record as invalid via `sink.EndRecord(commit: false)`.
- The engine writes the raw rejected JSONL line to `Console.Error` (`stderr`).
- The engine writes progress and informational logs to `Console.Out` (`stdout`).

Redirect `stderr` to capture all rejected records into a separate dead-letter log file:

```bash
# Capture rejected records into dlq.jsonl while ingesting valid records
Glacier.Forge --db postgres --table public.events --file incoming.jsonl 2> dlq.jsonl
```

### 7.3 Inspecting and Reprocessing Dead-Letter Records

Inspect the dead-letter log to diagnose format mismatches:

```bash
head -n 5 dlq.jsonl
```

After correcting schema definitions or applying necessary `--map` flags, reprocess the dead-letter file:

```bash
Glacier.Forge --db postgres --table public.events --file dlq.jsonl 2> dlq_unresolved.jsonl
```

---

## 8. Licensing and Audit Compliance

Glacier.Forge is **100% Free and Open-Source software under the MIT License**. You do not need a license key to run the software in production.

### 8.1 Enterprise Cryptographic License Verification

If your organization requires cryptographic provenance tracking or automated enterprise license auditing, you can supply an ECDSA P-256 license key.

You can supply the license key using three methods:
1. **CLI Flag**: `Glacier.Forge ... --key "PayloadBase64.SignatureBase64"`
2. **Environment Variable**: Set the `GLACIER_LICENSE` environment variable.
3. **Local File**: Place a text file named `license.lic` in the application working directory.

### 8.2 License Verification Architecture

```text
License Token Structure:
[ Base64 JSON Payload ] . [ Base64 IEEE P1363 Signature ]
```

The payload contains the licensed product, licensee email, subscription tier, and subscription expiration timestamp:

```json
{
  "Product": "Glacier.Forge",
  "Email": "enterprise@customer.com",
  "Tier": "Enterprise",
  "SubscriptionEnd": "2027-12-31T23:59:59Z"
}
```

The verification process:
1. Decodes the base64 payload and signature.
2. Verifies the SHA-256 digital signature against the public key using `ECDsa.VerifyData`.
3. Validates the product name (`Glacier.Forge`).
4. Compares the assembly build date against `SubscriptionEnd`. If the binary build date is earlier than or equal to the subscription end date, the license remains permanently valid (perpetual fallback rule).

---

## 9. Benchmark Reproduction Instructions

You can reproduce the zero-allocation performance figures on physical hardware using the included BenchmarkDotNet test suite.

### 9.1 Build the Benchmark Suite

Navigate to the benchmark directory and build the project in Release configuration:

```bash
cd Glacier.Forge/src/Benchmarks
dotnet build -c Release
```

### 9.2 Execute the Benchmark Suite

Run the benchmark project:

```bash
dotnet run -c Release
```

### 9.3 Benchmark Methods Tested

The suite benchmarks three primary ingestion paths using 1,000 JSONL records:
- **`BenchmarkCustomZeroAllocParser`**: Measures the Glacier.Forge zero-allocation byte-space parser and 256-ary Trie validator.
- **`BenchmarkCustomZeroAllocParserWithMapping`**: Measures performance with custom field mappings active.
- **`BenchmarkSystemTextJson`**: Measures standard reflection-less `JsonSerializer.Deserialize<SampleRecord>`.

### 9.4 Expected Output Verification

Confirm the following criteria in the BenchmarkDotNet summary report:
1. **Allocated Memory**: Verify that `BenchmarkCustomZeroAllocParser` reports **0 B**.
2. **GC Collections**: Verify that `Gen 0`, `Gen 1`, and `Gen 2` columns report `-` or `0.0000`.
3. **Execution Time**: Mean latency should measure approximately **140–160 μs** per 1,000 records.

---

## 10. Diagnostic and Troubleshooting Matrix

| Symptom / Error Message | Probable Root Cause | Recommended Remediation Action |
| :--- | :--- | :--- |
| **`Error: Missing required database arguments.`** | The command omitted either `--db` or `--table`. | Provide both `--db <sqlserver\|postgres>` and `--table <name>`. |
| **`Error: Unsupported database type 'xyz'.`** | An unsupported database type was specified. | Set `--db` to `sqlserver` or `postgres`. |
| **`Error: Connection string not provided...`** | The connection string is missing from both CLI arguments and configuration. | Supply `--conn "<connection_string>"` or set the appropriate environment variable. |
| **`Error: No columns found for table 'xyz'.`** | The specified table does not exist, or the user lacks catalog read permissions. | Verify the table name and schema prefix. Verify database user permissions on catalog tables. |
| **`License Error: Cryptographic signature is invalid...`** | The license key was corrupted, truncated, or tampered with. | Re-copy the exact license token string without added whitespace or line breaks. |
| **`License Error: Your subscription ended on...`** | The binary was compiled after the license subscription period ended. | Renew your license subscription, or download an older binary built before the expiration date. |
| **High rejection count on `stderr`** | JSON property names do not match database columns, or non-nullable columns are missing. | Inspect rejected records in `stderr`. Add the `--map` argument to map mismatched field names. |
| **`Npgsql.PostgresException: relation does not exist`** | PostgreSQL table name is missing its schema qualification. | Use a schema-qualified table name (e.g., `public.my_table`). |
| **`SqlException: Cannot find table...`** | SQL Server table name is missing its schema qualification. | Use a schema-qualified table name (e.g., `dbo.my_table`). |
