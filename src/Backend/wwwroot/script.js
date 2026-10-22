document.addEventListener('DOMContentLoaded', () => {
    // 1. Smooth scrolling for navigation links (excluding bootstrap pill/tab triggers)
    const navLinks = document.querySelectorAll('a[href^="#"]');
    navLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            if (this.hasAttribute('data-bs-toggle')) return;
            const targetId = this.getAttribute('href');
            if (!targetId || targetId === '#') return;

            const targetElement = document.querySelector(targetId);
            if (targetElement) {
                e.preventDefault();
                const navbar = document.querySelector('.navbar');
                const navbarHeight = navbar ? navbar.offsetHeight : 0;
                const targetPosition = targetElement.getBoundingClientRect().top + window.pageYOffset - navbarHeight;

                window.scrollTo({
                    top: targetPosition,
                    behavior: 'smooth'
                });
            }
        });
    });

    // 2. Console message to showcase Glacier software performance values
    console.log(
        '%c GLACIER SOFTWARE %c Central Documentation Hub v2.0 Initialized %c',
        'background: #0b0c10; color: #66fcf1; padding: 4px 6px; font-weight: bold; border-radius: 4px 0 0 4px;',
        'background: #1f2833; color: #f8f9fa; padding: 4px 6px; border-radius: 0 4px 4px 0;',
        'background: transparent;'
    );

    // 3. Toast Notification Helper
    function showToast(message) {
        let toast = document.getElementById('hub-toast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'hub-toast';
            toast.className = 'copy-toast';
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.classList.add('show');
        setTimeout(() => {
            toast.classList.remove('show');
        }, 2500);
    }

    // 4. Ecosystem Repository Matrix Filter and Search Engine
    const searchInput = document.getElementById('repo-search-input');
    const filterPills = document.querySelectorAll('.pillar-filter-pills .filter-pill');
    const matrixCards = document.querySelectorAll('.matrix-card');
    const countDisplay = document.getElementById('repo-match-count');

    let activePillar = 'all';
    let searchQuery = '';

    function filterRepositories() {
        let visibleCount = 0;
        matrixCards.forEach(card => {
            const cardPillar = card.getAttribute('data-pillar') || '';
            const cardKeywords = (card.getAttribute('data-keywords') || '').toLowerCase();
            const cardTitle = (card.querySelector('.matrix-repo-title') ? card.querySelector('.matrix-repo-title').textContent : '').toLowerCase();
            const cardDesc = (card.querySelector('.matrix-card-desc') ? card.querySelector('.matrix-card-desc').textContent : '').toLowerCase();

            const matchesPillar = (activePillar === 'all' || cardPillar === activePillar);
            const matchesSearch = !searchQuery || 
                cardTitle.includes(searchQuery) || 
                cardDesc.includes(searchQuery) || 
                cardKeywords.includes(searchQuery);

            if (matchesPillar && matchesSearch) {
                card.style.display = 'flex';
                visibleCount++;
            } else {
                card.style.display = 'none';
            }
        });

        if (countDisplay) {
            countDisplay.textContent = visibleCount;
        }
    }

    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            searchQuery = e.target.value.trim().toLowerCase();
            filterRepositories();
        });
    }

    filterPills.forEach(pill => {
        pill.addEventListener('click', function() {
            filterPills.forEach(p => p.classList.remove('active'));
            this.classList.add('active');
            activePillar = this.getAttribute('data-filter') || 'all';
            filterRepositories();
        });
    });

    // 5. Interactive Architecture Diagram Node Inspector
    const diagramStepNodes = document.querySelectorAll('.diag-step-node');

    const stepData = {
        // Forge Steps
        'forge-1': {
            title: '1. Streaming Ingress & Zero-Copy Hybrid I/O',
            badge: 'Glacier.Forge/HybridIoDispatcher.cs',
            desc: 'Dynamically routes files based on size threshold: files < 1 MB rent buffers from ArrayPool<byte>.Shared and invoke OS RandomAccess.Read with FileOptions.SequentialScan. Files >= 1 MB map unmanaged pointers directly via MemoryMappedFile and CreateViewAccessor with 1GB line-aligned reverse-newline boundary chunking.',
            alloc: 'Managed Allocations: 0 Bytes | Time: ~42 μs'
        },
        'forge-2': {
            title: '2. Zero-Transcoding UTF-8 Tokenizer',
            badge: 'Glacier.Forge/Program.cs:ProcessLine',
            desc: 'Processes incoming byte slices directly using System.Text.Json Utf8JsonReader without transcoding to UTF-16 strings. Nested objects or arrays are skipped instantaneously via reader.Skip(), maintaining zero allocation overhead throughout the parsing hot loop.',
            alloc: 'Managed Allocations: 0 Bytes | Hot Path: ValueSpan slicing'
        },
        'forge-3': {
            title: '3. Byte-Space 256-ary Trie Schema Validator',
            badge: 'Glacier.Forge/SchemaValidator.cs',
            desc: 'Traverses in-memory 256-ary Trie in O(L) time where L is property name length in bytes. Case insensitivity is executed via an aggressive-inlined bit shift (b - 32). Primitive type checks invoke System.Buffers.Text.Utf8Parser.TryParse directly on raw byte spans.',
            alloc: 'Managed Allocations: 0 Bytes | Complexity: O(L) trie lookup'
        },
        'forge-4': {
            title: '4. Direct Relational Bulk Sinks',
            badge: 'Glacier.Forge/SqlServerSink.cs & PostgresSink.cs',
            desc: 'PostgreSQL sink streams contiguous binary tuples directly via NpgsqlBinaryImporter using COPY BINARY. SQL Server sink buffers rows into PooledBatchDataReader, passing unmanaged SqlChars slices straight to SqlBulkCopy with zero intermediate string instances.',
            alloc: 'Latency: 144.8 μs per 1,000 records | GC Collections: 0'
        },

        // Inference Steps
        'inf-1': {
            title: '1. Zero-Copy Model Ingestion (GGUF & Safetensors)',
            badge: 'Glacier.Inference/Formats/GgufFile.cs',
            desc: 'Loads multi-gigabyte models in under 15ms cold startup via MemoryMappedFile pointers. Parses metadata key-values and tensor descriptors directly from unmanaged byte space, avoiding GC heap allocations during model bootstrapping.',
            alloc: 'Startup Time: < 15 ms cold | Managed Allocations: 0 Bytes'
        },
        'inf-2': {
            title: '2. Unmanaged Ring-Buffered KV Cache',
            badge: 'Glacier.Inference/Memory/KVCache.cs',
            desc: 'Allocates contiguous unmanaged buffers via NativeMemory.AllocZeroed. Physical slots are calculated via ring-buffer modulo arithmetic (slot = pos % EffectiveWindowSize), supporting sliding window attention without memory reallocation.',
            alloc: 'Physical Stride: layerStride x headStride x slot | Allocation: 0 Bytes/tok'
        },
        'inf-3': {
            title: '3. Autoregressive Transformer Block & RoPE',
            badge: 'Glacier.Inference/Layers/TransformerBlock.cs',
            desc: 'Fuses RMSNorm, QKV projection via quantized GEMV kernels, and Rotary Position Embeddings (1D NeOX, 2D Axial for FLUX.1, or 3D Spatio-Temporal for Wan 2.1). Fused SwiGLU / GeLU FFN layers execute with zero intermediate heap buffers.',
            alloc: 'Throughput: > 50 tokens/sec decode on RTX 4060'
        },
        'inf-4': {
            title: '4. Multi-Backend Hardware HAL',
            badge: 'Glacier.Inference/Gpu/ & CPU SIMD',
            desc: 'Direct dispatch across Direct3D 12 Wave32 HLSL compute shaders, Vulkan 1.1 SPIR-V compute kernels with VK_KHR_cooperative_matrix, direct NVIDIA SASS via nvcuda.dll, AMD ROCm/HIP via amdhip64.dll, and multi-threaded AVX-512 CPU SIMD.',
            alloc: 'Zero Native C++ DLLs | Pure C# P/Invoke to hardware drivers'
        },
        'inf-5': {
            title: '5. Speculative Decoding & GPU Sampling',
            badge: 'Glacier.Inference/Engine/SpeculativeEngine.cs',
            desc: 'Accelerates autoregressive generation using N-gram prompt lookup draft verification in a single batched target forward pass. Fused GPU warp-shuffle argmax kernel selects output tokens with zero host-device synchronization stalls.',
            alloc: 'Speculative Boost: 1.8x - 2.4x speedup on dense text generation'
        },

        // Gpu Steps
        'gpu-1': {
            title: '1. Lock-Free Sequence Counter & Turn Lease',
            badge: 'Glacier.Gpu/RingBuffer/PersistentRingBuffer.cs',
            desc: 'Producer claims circular slot via atomic Interlocked.Increment on sequence counter. Producer awaits slot lease turn using Dmitry Vyukov bounded MPMC queue algorithm. Arrays are padded with CacheLineStride = 8 to prevent CPU false sharing.',
            alloc: 'Dispatch Latency: 100 ns | Lock-Free Invariant: Non-blocking'
        },
        'gpu-2': {
            title: '2. 64-Byte Cacheline-Aligned GpuWorkTask',
            badge: 'Glacier.Gpu/RingBuffer/GpuWorkTask.cs',
            desc: 'Task descriptor is strictly defined with [StructLayout(LayoutKind.Sequential, Size = 64)], perfectly fitting a modern CPU cache line. Contains TaskId, OpCode, ElementCount, 64-bit unmanaged buffer pointers BufferA/B/C/D, Scalar, and Status.',
            alloc: 'Struct Size: Exactly 64 bytes | Memory Alignment: Cacheline isolated'
        },
        'gpu-3': {
            title: '3. Persistent Worker Megakernel Polling',
            badge: 'Glacier.Gpu/Compilation/KernelCompiler.cs',
            desc: 'Supersonic worker megakernel resident on GPU Streaming Multiprocessors continuously polls host-pinned memory for task->Status == 0. Bypasses traditional cuLaunchKernel OS driver overhead (8-12 μs) down to 100 nanoseconds dispatch latency.',
            alloc: 'Dispatch Throughput: > 10,000,000 tasks/sec | Turnaround: 4.2 μs'
        },
        'gpu-4': {
            title: '4. Warp Lockstep Execution & SASS JIT',
            badge: 'Glacier.Gpu/Drivers/CuDriver.cs',
            desc: 'JIT-compiles PTX to bare-metal SASS cubin machine code via ptxas.exe across Ada Lovelace (sm_89), Ampere (sm_86/sm_80), and Turing (sm_75). 32 threads per warp execute vector math in lockstep with zero runtime wrapper overhead.',
            alloc: 'Hardware Utilization: 13.6 TFLOPS FP32 compute on RTX 4060'
        },
        'gpu-5': {
            title: '5. Heterogeneous dGPU + APU Zero-Copy RAM Co-Execution',
            badge: 'Glacier.Gpu/Engines/HeterogeneousEngine.cs',
            desc: 'Simultaneously streams compute-dense FP32 FMA loops to discrete NVIDIA GPU while routing high-bandwidth reductions across AMD APU unified DDR5/LPDDR5X physical RAM (34+ GB/s) at sub-300ns coherency latency.',
            alloc: 'Memory Transfer: 0 B PCIe transfer on APU | Latency: 300 ns coherency'
        }
    };

    diagramStepNodes.forEach(node => {
        node.addEventListener('click', function() {
            const stepId = this.getAttribute('data-step');
            const targetInspectorId = this.getAttribute('data-target-inspector');
            const inspector = document.getElementById(targetInspectorId);
            
            // Remove active from sibling nodes in the same canvas
            const parentFlow = this.closest('.diagram-step-flow');
            if (parentFlow) {
                parentFlow.querySelectorAll('.diag-step-node').forEach(n => n.classList.remove('active'));
            }
            this.classList.add('active');

            if (inspector && stepData[stepId]) {
                const data = stepData[stepId];
                inspector.querySelector('.diag-inspector-title').textContent = data.title;
                inspector.querySelector('.diag-inspector-badge').textContent = data.badge;
                inspector.querySelector('.diag-inspector-desc').textContent = data.desc;
                inspector.querySelector('.diag-inspector-alloc').textContent = data.alloc;
            }
        });
    });

    // 6. Starter Kit Templates Content & Downloader
    const templates = {
        'readme': `# Glacier.{ModuleName}
<!-- ASD-STE100 & Diátaxis Compliant README Template -->

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![AOT Compatible](https://img.shields.io/badge/Native%20AOT-Ready-success.svg)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![Zero-Allocation](https://img.shields.io/badge/Hot%20Path-Zero%20Allocation-66fcf1.svg)](https://glaciersoftware.dev)

High-performance, zero-allocation {ModuleDescription} built for C# .NET 10.

## Overview
Glacier.{ModuleName} delivers extreme-throughput systems engineering without managed garbage collection pauses.

## Key Features
- **Zero-Allocation Hot Paths:** Operates entirely over unmanaged spans and contiguous buffers.
- **Hardware Acceleration:** Native SIMD intrinsics (Vector512, Vector256) and hardware compute dispatches.
- **Native AOT Ready:** Zero runtime reflection, fully trimmer-safe and container-optimized.

## Architecture
\`\`\`mermaid
flowchart LR
    A[Input Stream] --> B[Glacier.{ModuleName} Engine]
    B --> C[Zero-Copy Destination]
\`\`\`

## Physical Benchmark Results
*Environment: AMD Ryzen AI 9 HX 370 @ 2.00GHz, 32GB LPDDR5X, Windows 11 Enterprise.*

| Workload | Throughput | Managed Memory |
|:---|---:|---:|
| Benchmark Suite 1 | 14.2M ops/sec | 0 B |
| Benchmark Suite 2 | 225.8 ns / op | 0 B |

## Quickstart
\`\`\`bash
dotnet add package Glacier.{ModuleName}
\`\`\`

\`\`\`csharp
using Glacier.{ModuleName};

// Initialize engine with zero heap allocation
var engine = new {ModuleEngine}();
engine.Execute(dataSpan);
\`\`\`

## Documentation
- [Architecture Specification](docs/ARCHITECTURE.md)
- [Operational Usage Manual](docs/USAGE.md)
- [Ecosystem Central Hub](https://glaciersoftware.dev/#docs)

## License
MIT License. Copyright (c) 2026 Glacier Software.`,

        'architecture': `# Glacier.{ModuleName} Architecture Specification
<!-- Arc42 Architecture Template (Version 8.2) -->

## 1. Introduction and Goals
Glacier.{ModuleName} provides ultra-low latency {EnginePurpose} for the Glacier ecosystem.

### Quality Goals
1. Zero managed heap allocations on all execution hot paths.
2. Complete Native AOT compatibility with zero reflection.
3. Sub-microsecond dispatch latency across physical hardware.

## 2. Architecture Constraints
- Target Framework: .NET 10.0
- Memory Model: Unmanaged \`NativeMemoryOwner<T>\` and \`ReadOnlySpan<T>\`.
- Concurrency: Lock-free algorithms with explicit volatile memory barriers.

## 3. Context and Scope
Glacier.{ModuleName} operates as a foundational component in the Glacier architecture.
\`\`\`mermaid
flowchart TD
    Upstream[Upstream Producer] --> Engine[Glacier.{ModuleName}]
    Engine --> Downstream[Downstream Consumer]
\`\`\`

## 4. Solution Strategy
- Employs cacheline-aligned structs ([StructLayout(LayoutKind.Sequential, Size = 64)]).
- Bypasses managed runtime abstractions in favor of direct hardware intrinsics.

## 5. Building Block View
- **Core Engine:** Orchestrates task queues and memory lifecycles.
- **Compute Kernels:** Vectorized hardware operations.
- **Memory Subsystem:** Pinned unmanaged buffer pools.

## 6. Runtime View
1. Caller acquires unmanaged buffer slice.
2. Engine dispatches hardware kernel with zero host-device synchronization stalls.
3. Memory returned to pool with atomic reference decrement.

## 7. Deployment View
Distributed as a standalone NuGet package and Native AOT dynamic library.

## 8. Cross-Cutting Concepts
- Logging: Pluggable zero-dependency \`GlacierDiagnostics\` / \`IGlacierLogger\`.
- Error Handling: Explicit exceptions before hot paths; no silent error swallowing.

## 9. Architecture Decisions (ADRs)
- **ADR-001:** Avoid managed collections (\`List<T>\`, \`Dictionary<K,V>\`) on inner loops.
- **ADR-002:** Enforce Little-Endian binary blitting for network and disk serialization.

## 10. Quality Scenarios
- **Stress:** Continuous operation under 100M+ elements without memory fragmentation.
- **AOT:** Zero trim warnings during compilation.

## 11. Risks and Technical Debt
- Hardware variance across non-AVX-512 architectures is mitigated via dynamic CPU capability checks.

## 12. Glossary
- **Zero-Allocation:** Execution path that causes 0 managed GC allocations.
- **Contiguous Buffer:** Single block of unmanaged physical memory.`,

        'usage': `# Glacier.{ModuleName} Operational Usage Manual
<!-- Diátaxis How-To & Reference Manual -->

## CLI Reference
Run the engine via command line or terminal:
\`\`\`bash
Glacier.{ModuleName}.Cli --input <path> --output <path> [--threads 8] [--verbose]
\`\`\`

### Parameters
| Flag | Required | Default | Description |
|:---|:---:|:---:|:---|
| \`--input\`, \`-i\` | Yes | - | Input file or streaming endpoint. |
| \`--output\`, \`-o\` | Yes | - | Destination file or relational sink. |
| \`--threads\`, \`-t\` | No | CPU Cores | Number of parallel worker threads. |

## C# API Reference

### 1. Basic In-Memory Processing
\`\`\`csharp
using Glacier.{ModuleName};

// Create unmanaged buffer
using var buffer = new NativeMemoryOwner<byte>(1024 * 1024);
Span<byte> span = buffer.Span;

// Execute processing
var result = {ModuleEngine}.Process(span);
\`\`\`

### 2. High-Throughput Streaming Pipeline
\`\`\`csharp
using System.IO.Pipelines;
using Glacier.{ModuleName};

var pipe = new Pipe();
await {ModuleEngine}.StreamAsync(pipe.Reader, targetSink);
\`\`\`

## Troubleshooting & Diagnostics
- **Exception: OutOfMemoryException:** Verify that system commit charge is sufficient for unmanaged buffer pools.
- **Non-Zero Allocations Detected:** Confirm that caller is passing \`Span<T>\` rather than boxed arrays.`,

        'diagrams': `# Glacier.{ModuleName} Architectural Diagram Suite
<!-- Native GitHub Mermaid Diagrams with C4 Modeling -->

## Level 1: System Context Diagram
\`\`\`mermaid
flowchart TD
    User([System Consumer])
    subgraph Glacier [Glacier Ecosystem]
        Engine[Glacier.{ModuleName}]
        Storage[(Unmanaged Memory Pool)]
    end
    User -->|Feeds Data Streams| Engine
    Engine -->|Allocates Zero-Copy| Storage
\`\`\`

## Level 2: Container Diagram
\`\`\`mermaid
flowchart TD
    subgraph Host Process
        CLI[Command Line Host]
        Core[Engine Core Assembly]
        HAL[Hardware Abstraction Layer]
    end
    CLI --> Core
    Core --> HAL
    HAL --> Hardware[(GPU VRAM / APU RAM)]
\`\`\`

## Level 3: Component Diagram
\`\`\`mermaid
flowchart TD
    Reader[Stream Ingest Reader] --> Tokenizer[Zero-Transcoding Parser]
    Tokenizer --> Validator[Byte-Space Trie Matcher]
    Validator --> Sink[Direct Relational Sink]
\`\`\`

## Memory Layout & Buffer Lifecycle
\`\`\`mermaid
sequenceDiagram
    autonumber
    participant Host as C# Managed Thread
    participant Pool as Unmanaged Memory Block
    participant Dev as Hardware Execution Unit

    Host->>Pool: Borrow Contiguous Slice (NativeMemoryOwner)
    Host->>Dev: Dispatch Vectorized Kernel
    Dev-->>Host: Signal Execution Complete
    Host->>Pool: Retain or Release Buffer Slice
\`\`\``
    };

    const templateTabs = document.querySelectorAll('.template-tab-btn');
    const templateCodeDisplay = document.getElementById('template-code-display');
    const templateCopyBtn = document.getElementById('template-copy-btn');
    const templateDownloadBtn = document.getElementById('template-download-btn');
    let currentTemplateKey = 'readme';

    function setTemplate(key) {
        currentTemplateKey = key;
        if (templateCodeDisplay && templates[key]) {
            templateCodeDisplay.textContent = templates[key];
        }
    }

    templateTabs.forEach(btn => {
        btn.addEventListener('click', function() {
            templateTabs.forEach(b => b.classList.remove('active'));
            this.classList.add('active');
            const key = this.getAttribute('data-template');
            setTemplate(key);
        });
    });

    if (templateCopyBtn) {
        templateCopyBtn.addEventListener('click', () => {
            const code = templates[currentTemplateKey] || '';
            navigator.clipboard.writeText(code).then(() => {
                showToast(`✓ Copied ${currentTemplateKey.toUpperCase()} template to clipboard!`);
            }).catch(err => {
                console.error('Failed to copy: ', err);
            });
        });
    }

    if (templateDownloadBtn) {
        templateDownloadBtn.addEventListener('click', () => {
            const code = templates[currentTemplateKey] || '';
            const filename = `${currentTemplateKey.toUpperCase()}_TEMPLATE.md`;
            const blob = new Blob([code], { type: 'text/markdown;charset=utf-8;' });
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = filename;
            link.click();
            URL.revokeObjectURL(link.href);
            showToast(`✓ Downloaded ${filename}`);
        });
    }

    // 7. Generic Copy Button Handler for Pre Code Blocks
    const genericCopyButtons = document.querySelectorAll('.btn-copy-code');
    genericCopyButtons.forEach(btn => {
        btn.addEventListener('click', function() {
            const targetSelector = this.getAttribute('data-clipboard-target');
            const targetElement = document.querySelector(targetSelector);
            if (targetElement) {
                const textToCopy = targetElement.textContent;
                navigator.clipboard.writeText(textToCopy).then(() => {
                    showToast('✓ Copied code to clipboard!');
                }).catch(err => {
                    console.error('Failed to copy: ', err);
                });
            }
        });
    });

    // Initialize with default template
    setTemplate('readme');
});
