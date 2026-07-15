# Glacier.Forge Ingestion Engine Usage Manual

This document provides comprehensive instructions, architectural nuances, and production examples for running **Glacier.Forge** (the zero-allocation, high-performance C# ingestion engine).

---

## Conceptual Ingestion Pipeline

Glacier.Forge is designed as a zero-copy data hardener between non-deterministic token sources (like LLM outputs) and strictly-typed relational databases. 

```text
  [LLM / Raw JSONL Stream]
             │
             ▼  (stdin pipe or local file block read)
  [Glacier.Forge Engine]
             │
             ├─► [Zero-Allocation Utf8JsonReader Loop]
             ├─► [Byte-Space Case-Insensitive Trie Validator]
             ├─► [Strict Schema Mapping Check]
             │
             ▼  (valid rows)
  [SQL Server / PostgreSQL Sink] ──► (Bulk Load Transaction Commit)
```

---

## Command Line Arguments

To run the compiled binary:
```bash
Glacier.Forge --db <sqlserver|postgres> --table <schema.table_name> [--file <file_path>]
```

| Flag | Required | Allowed Values | Description |
| :--- | :--- | :--- | :--- |
| `--db` | **Yes** | `sqlserver` \| `postgres` | Target database type. |
| `--table` | **Yes** | String (e.g., `customers` or `dbo.users`) | Target database table name. For Postgres, can be schema-qualified (e.g., `public.users`). |
| `--file` | No | Path to local `.jsonl` file | If specified, runs in file ingestion mode. If omitted, the engine streams inputs directly from Standard Input (`stdin`). |

---

## Configuration & Schema Resolution

### 1. How Database Credentials are Resolved
Glacier.Forge checks the execution environment for database connection settings using the standard .NET configuration provider chain:
* **Local Configuration**: Reads `appsettings.json` located in the folder of the running binary under the `ConnectionStrings` node.
* **Environment Variables**: Resolves system environment keys matching `ConnectionStrings__DefaultConnection` (ideal for stateless container runtimes like AWS App Runner or Docker).
* **Development Secret Store**: During local CLI development, it checks the local C# Secret Manager store (`dotnet user-secrets`).

### 2. How the Database & JSON Schema is Discovered
Glacier.Forge is designed to run **dynamically** without needing any manual schema code edits or predefined JSON Schema files:
* **Dynamic Database Schema Lookup**: At startup, the engine connects to the target database and queries the database catalog (e.g. `INFORMATION_SCHEMA.COLUMNS` in SQL Server, or `pg_catalog` views in Postgres) for the specific table name passed to the `--table` parameter. It resolves column names, exact data types, and nullability, constructing a fast in-memory schema schema map.
* **Schema-Driven JSON Validation**: The resolved database schema acts as the validation source of truth. As JSON records stream in, the parser dynamically matches the keys against the fetched column map. If a value type mismatches (e.g., a text string sent to an integer column) or a non-nullable column is omitted, the record is rejected, protecting database integrity on-the-fly.

---

## Input JSONL Format Rules & Nuances

Each line in the file or standard input stream must represent a single, self-contained JSON Object.

### 1. Structure
* **One Object per Line**: Every record must start with `{` and end with `}` on a single physical line.
* **Separators**: Lines must be separated by `\n` or `\r\n`.

### 2. Schema Matching
* **Case-Insensitive Keys**: Property names in the JSON object are matched to database columns using a case-insensitive Trie. `userId`, `userid`, and `USERID` will all resolve to the database column `UserId`.
* **Superfluous Fields**: Extra properties in the JSON record that do not exist in the database table are automatically ignored. No error is thrown.
* **Complex/Nested Structures**: If a property is an array or a sub-object, the engine utilizes `reader.Skip()` to traverse past it without parsing or allocating, protecting the pipeline against nested structure crash vectors.

### 3. Null Values & Missing Fields
* **Missing Fields**: If a database column is omitted in a JSON line, it will be written to the database as `NULL` (provided the database column is nullable).
* **Explicit Nulls**: Values defined as `"my_col": null` are recognized and mapped to database `NULL`.
* **Type-Mapping Table**:
  
  | C# Parse Method | Supported Input Tokens | Expected DB Target Types |
  | :--- | :--- | :--- |
  | `Utf8Parser.TryParse(..., out int)` | Numbers, digit bytes | `int`, `integer`, `int4`, `bigint` |
  | `Utf8Parser.TryParse(..., out DateTime)` | ISO 8601 strings (e.g., `"2026-07-15T18:00:00Z"`) | `datetime2`, `timestamp`, `timestamptz` |
  | Direct Byte Slice mapping | True/False literals (`"active": true`, `"active": 1`) | `bit`, `boolean`, `bool` |
  | UTF-8 Byte Span | Quoted string values | `varchar`, `nvarchar`, `text` |

---

## Detailed Pipeline Examples

### Example 1: Streaming via Standard Input (`stdin`)
This is the recommended setup when connecting Glacier.Forge directly to a continuous data source, such as a Python script generation loop or curl stream:

```bash
# Pipes output from a generator script directly into Glacier.Forge
python generate_llm_records.py | Glacier.Forge --db postgres --table public.raw_logs
```
*Nuance: Glacier.Forge processes lines as they arrive. If the generation source slows down, Glacier.Forge buffers records in-memory and executes database flush queries only when the batch limit (1,000 records) is reached or when standard input is closed.*

### Example 2: Ingesting Large Files (Hybrid I/O)
To load a massive local backup or dataset:
```bash
Glacier.Forge --db sqlserver --table dbo.billing_history --file C:\backups\2026_data.jsonl
```
*Nuance: If the file size is less than 1MB, Glacier.Forge rents a single byte buffer from `ArrayPool<byte>` to load the file into memory. If the file is 1MB or larger, the engine maps the file directly using OS Memory Mapped Files (`MMF`), allowing zero-copy processing of files exceeding 2GB.*

---

## Licensing Setup

Glacier.Forge requires an ECDSA signed license key to run. 

### 1. Environment Variable (Recommended for production)
Set the license key as an environment variable:
```bash
# Windows (PowerShell)
$env:GLACIER_LICENSE="<base64_payload>.<base64_signature>"

# Linux / macOS
export GLACIER_LICENSE="<base64_payload>.<base64_signature>"
```

### 2. Local License File
Create a file named `license.lic` in the execution directory containing the raw license key:
```text
eyJQaW5nIjogIm9rIn0=.MEUCIQ...
```

---

## Error Handling & Troubleshooting

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| **"License Error: ..."** | Invalid signature, tampered payload, or expired subscription date. | Renew the subscription or ensure the local clock matches the license period. |
| **"SQL Server/Postgres table not found"** | The target database connection string is wrong or the table does not exist. | Configure development connections via `dotnet user-secrets` inside the `src/Glacier.Forge` folder. |
| **Skipped Lines in Console** | The line contains malformed JSON (e.g. missing commas, unclosed brackets) or a non-nullable column received an invalid data type. | Glacier.Forge logs skipped line details to `stderr`. Inspect standard error logs for format mismatches. |
