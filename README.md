# Glacier.Forge

A zero-allocation, high-performance C# ingestion engine that parses semantic JSONL tokens stream and bulk-loads them directly into SQL Server and PostgreSQL databases.

Designed for performance-critical scenarios (such as receiving high-velocity LLM token generation pipelines), this engine maintains a **0-byte managed memory allocation** hot path, neutralizing Garbage Collector pause latencies.

---

## Directory Structure

We organize the workspace following standard .NET layout conventions:

```text
├── src/
│   ├── Glacier.Forge/                # Main ingestion engine CLI application
│   │   ├── Program.cs                # Entry point, CLI argument parsing, streaming loops
│   │   ├── IRelationalSink.cs        # Relational sink database interface
│   │   ├── SqlServerSink.cs          # Bulk-loader integration for MS SQL Server
│   │   ├── PostgresSink.cs           # Binary COPY bulk-loader integration for Postgres
│   │   ├── SchemaValidator.cs        # Byte-space Trie matching and format validator
│   │   ├── HybridIoDispatcher.cs     # Zero-copy file dispatcher (MMFs vs Rented buffers)
│   │   ├── LicenseManager.cs         # Cryptographic license checker (Native AOT safe)
│   │   ├── appsettings.json          # Production database and app configuration placeholders
│   │   └── Glacier.Forge.csproj
│   │
│   ├── Backend/                      # Web API for licensing validation and key generation
│   │   ├── Program.cs                # HMAC webhook endpoints & ECDSA signature creation
│   │   └── Backend.csproj
│   │
│   └── Benchmarks/                   # Performance measurement suite using BenchmarkDotNet
│       ├── Program.cs                # Ingestion throughput and memory benchmarks
│       └── Benchmarks.csproj
│
├── tests/
│   └── Glacier.Forge.Tests/          # Complete xUnit validation and integration tests
│       ├── ParserTests.cs            # Ingestion pipeline, file stream and license unit tests
│       ├── ValidatorTests.cs         # Trie match and value parsing tests
│       └── Glacier.Forge.Tests.csproj
│
└── README.md                         # Project documentation
```

---

## Architectural Deep-Dive

### 1. Ingest Pipeline & Zero-Transcoding Parsing
*   **Utf8JsonReader Loop**: JSONL rows from `stdin` or local files are read as raw UTF-8 `ReadOnlySpan<byte>` arrays. We pass this span directly to `System.Text.Json.Utf8JsonReader` to traverse properties without transcoding bytes to UTF-16 C# character strings.
*   **Nested Property Skipping**: To protect against desynchronization when parsing arbitrary LLM-generated nested objects or arrays, we invoke `reader.Skip()` when an unknown property's value token is `StartObject` or `StartArray`.
*   **Literal Resolving**: Built-in JSON literals (like `true`, `false`, `null`) are mapped to fixed UTF-8 literals (e.g. `"true"u8`) directly via token type examination, bypassing the empty value span return of `reader.ValueSpan`.

### 2. Byte-Space Trie Schema Validator
*   **High-Speed Trie Lookup**: Matches incoming column names to target schemas in $O(N)$ time (where $N$ is property length). The Trie is built using a flat array of 256 references per node, allowing direct byte-index array lookups.
*   **Case-Insensitive Byte Hashing**: Character case insensitivity is evaluated directly in byte space by bit-mapping ASCII lowercase letters (`a-z`) to uppercase (`A-Z`) via an Aggressive-Inlined bit shift (`byte - 32`), executing with zero dictionary allocations or string conversions.
*   **Allocation-Free Format Validation**: Validation of database formats (e.g. integer, dates) is done by feeding the `Utf8JsonReader` by `ref` directly into the validation logic. This leverages the runtime's internal date and primitive parser (`reader.TryGetDateTime()`), eliminating memory allocation during validation.

### 3. High-Performance Database Sinks
*   **Postgres COPY Text Advantage**: Writes text and varchar columns directly to `NpgsqlBinaryImporter` as raw UTF-8 `ReadOnlyMemory<byte>` slices of our rented batch buffer. This completely bypasses the CPU tax of converting string data to C# characters, giving Postgres load performance a massive boost.
*   **SQL Server Bulk Data Reader**: Implements a custom `DbDataReader` subclass (`PooledBatchDataReader`) that buffers records in pre-allocated primitive columns. It uses `System.Data.SqlTypes.SqlChars` wrappers pointing to slices of the pre-allocated string buffers to output data to `SqlBulkCopy` without triggering string allocations.
*   **Postgres Connection/Transaction Safety**: To prevent holding database connections and transactions open during slow/intermittent input streams, `PostgresSink` buffers elements in-memory first. The PostgreSQL connection and binary `COPY` transactions are opened exclusively during `FlushBatchAsync()` commits.

### 4. Zero-Copy Hybrid I/O Dispatching
To ingest files efficiently:
*   **Small Files (< 1MB)**: Rented buffers from `ArrayPool<byte>` and OS-level `RandomAccess` read bypass `FileStream` stream allocations.
*   **Large Files (>= 1MB)**: Uses `MemoryMappedFile` view accessors to expose raw memory pointers (`byte*`) directly as `ReadOnlySpan<byte>`, allowing copy-free ingestion of massive multi-gigabyte datasets.

### 5. Cryptographic Licensing Engine (Native AOT Compatible)
To secure distribution while retaining performance:
*   **Native AOT Safe Deserialization**: Uses JSON source generators (`JsonSerializerContext`) to parse inputs without reflection, allowing compilation into a single, self-contained native binary (`PublishAot = true`).
*   **Strict Deserialization Validation**: Decorated the properties in `LicensePayload` with C# 11 `required` and `init` modifiers. This forces the serializer to throw exceptions if tampered payloads omit fields, avoiding bypass vulnerabilities.
*   **Perpetual Fallback Rule**: Extracts the CLI compilation timestamp from MSBuild informational assembly version properties and compares it to the license's subscription end date.
*   **Constant-Time Verification**: The webhook backend uses `CryptographicOperations.FixedTimeEquals` for HMAC comparison, neutralizing timing attack vectors on signature validation.

---

## Development Setup

### 1. Database Credentials Configuration
We use .NET's Secret Manager to store development database connections. Keep connection details out of JSON files.

Initialize the secrets inside the CLI engine project (`src/Glacier.Forge`):
```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=<SQL_SERVER_IP>;Database=HardenDb;User Id=sa;Password=<YOUR_PASSWORD>;TrustServerCertificate=True"
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=<POSTGRES_IP>;Database=postgres;Username=postgres;Password=<YOUR_PASSWORD>"
```

Initialize webhook secret and private key inside the Web API backend (`src/Backend`):
```bash
dotnet user-secrets init
dotnet user-secrets set "LemonSqueezy:WebhookSecret" "YOUR_WEBHOOK_SECRET_KEY"
dotnet user-secrets set "LemonSqueezy:PrivateKeyPem" "YOUR_ECDSA_PRIVATE_KEY_PEM"
```

*Note: During development, if `appsettings.json` is missing or contains placeholder values, the CLI automatically falls back to reading `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` dynamically.*

---

## Building and Running

### Building the Project
Restore and build the CLI in Release configuration:
```bash
dotnet build -c Release
```

### Running Tests
Execute xUnit unit and integration tests:
```bash
dotnet test
```

### Running Benchmarks
To run the memory and performance benchmarks:
```bash
cd src/Benchmarks
dotnet run -c Release
```

---

## Benchmarks Performance Summary

Benchmark results processing 1,000 JSONL records:

| Method | Mean Time | GC Generation 0 Collects | Allocated Memory |
| :--- | :--- | :--- | :--- |
| **BenchmarkCustomZeroAllocParser** | **146.5 μs** | **-** | **0 B** |
| `BenchmarkSystemTextJson` | 131.4 μs | 4.6387 | 39.06 KB |

By keeping the hot path entirely in byte space, the engine is **nearly as fast as simple reflection-less deserialization** while executing with **exactly 0 bytes of managed memory allocations**.
