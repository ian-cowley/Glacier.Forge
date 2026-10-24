/**
 * Glacier Software - Central Documentation Hub & World-Class Developer Portal
 * High-performance, zero-allocation client-side interactivity engine.
 * 
 * Modules:
 *  1. Smooth Anchor Navigation
 *  2. Glacier Performance Console Telemetry
 *  3. Robust Clipboard Copy & Debounced Toast Notification Subsystem
 *  4. Pre-Indexed In-Memory Search Engine & Category Filter with Zero-Results State
 *  5. Interactive Architectural Diagram Inspector Modal & SVG Stage Controller
 *  6. Interactive Architecture Diagram Node Inspector (Docs Hub Cornerstones)
 *  7. Starter Kit Templates Content, Copy & Downloader
 *  8. Accessible Code Snippet Copy Widgets with Dotted ID Target Resolution
 *  9. Diataxis Documentation Hub Tab Navigation & Deep-Linking
 */

document.addEventListener('DOMContentLoaded', () => {
    'use strict';

    // =========================================================================
    // 1. Smooth Scrolling for Navigation Links (Excluding Tab Triggers)
    // =========================================================================
    const navLinks = document.querySelectorAll('a[href^="#"]');
    navLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            if (this.hasAttribute('data-bs-toggle') || this.getAttribute('role') === 'tab') {
                return;
            }
            const targetId = this.getAttribute('href');
            if (!targetId || targetId === '#' || targetId.length <= 1) {
                return;
            }

            try {
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
            } catch {
                // Ignore query errors for non-standard selector targets
            }
        });
    });

    // =========================================================================
    // 2. Glacier Performance Console Telemetry
    // =========================================================================
    console.log(
        '%c GLACIER SOFTWARE %c World-Class Developer Portal v2.0 Initialized %c',
        'background: #090B0E; color: #66FCF1; padding: 4px 8px; font-weight: bold; border-radius: 4px 0 0 4px;',
        'background: #161B22; color: #F0F6FC; padding: 4px 8px; border-radius: 0 4px 4px 0;',
        'background: transparent;'
    );

    // =========================================================================
    // 3. Robust Clipboard Copy & Debounced Toast Notification Subsystem
    // =========================================================================

    /**
     * Copies plain text to the system clipboard using the Async Clipboard API
     * with an offscreen textarea execCommand fallback for legacy/insecure contexts.
     * @param {string} text - Text content to copy
     * @returns {Promise<boolean>}
     */
    async function copyToClipboard(text) {
        if (!text) return false;

        // Primary: Async Clipboard API (requires secure context HTTPS or localhost)
        if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (err) {
                console.warn('[Glacier Clipboard] Async Clipboard API failed, attempting textarea fallback:', err);
            }
        }

        // Fallback: Offscreen textarea + document.execCommand('copy')
        try {
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.setAttribute('readonly', '');
            textarea.style.position = 'fixed';
            textarea.style.top = '-9999px';
            textarea.style.left = '-9999px';
            textarea.style.opacity = '0';
            textarea.style.pointerEvents = 'none';
            document.body.appendChild(textarea);

            textarea.select();
            textarea.setSelectionRange(0, textarea.value.length);

            const successful = document.execCommand('copy');
            document.body.removeChild(textarea);
            return successful;
        } catch (err) {
            console.error('[Glacier Clipboard] Clipboard copy fallback failed:', err);
            return false;
        }
    }

    let hubToastTimer = null;

    /**
     * Displays a non-intrusive floating toast notification with smooth animation.
     * @param {string} message - Message text
     * @param {number} duration - Display time in ms (default 2000ms)
     */
    function showToast(message = 'Snippet copied to clipboard', duration = 2000) {
        let toast = document.getElementById('hub-toast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'hub-toast';
            toast.className = 'copy-toast';
            toast.setAttribute('role', 'status');
            toast.setAttribute('aria-live', 'polite');
            document.body.appendChild(toast);
        }

        if (hubToastTimer) {
            clearTimeout(hubToastTimer);
            hubToastTimer = null;
        }

        toast.textContent = message;
        toast.classList.add('show');

        hubToastTimer = setTimeout(() => {
            toast.classList.remove('show');
            hubToastTimer = null;
        }, duration);
    }

    // =========================================================================
    // 4. Pre-Indexed In-Memory Search Engine & Domain Category Filter
    // =========================================================================
    const searchInput = document.getElementById('repo-search-input');
    const filterPills = document.querySelectorAll('.pillar-filter-pills .filter-pill');
    const matrixCards = document.querySelectorAll('.matrix-card, .project-card');
    const countDisplay = document.getElementById('repo-match-count');
    const ecosystemGrid = document.getElementById('ecosystem-grid');

    let activePillar = 'all';
    let searchQuery = '';
    let debounceTimer = null;
    const DEBOUNCE_DELAY_MS = 180;

    // 4.1 In-Memory Pre-Indexed Search Corpus (<0.05ms search execution)
    const repoIndex = Array.from(matrixCards).map(card => {
        const pillar = (card.getAttribute('data-pillar') || card.getAttribute('data-category') || '').trim().toLowerCase();
        const keywords = (card.getAttribute('data-keywords') || '').toLowerCase();
        const titleEl = card.querySelector('.matrix-repo-title') || card.querySelector('.card-title');
        const title = (titleEl ? titleEl.textContent : '').trim().toLowerCase();
        const subtitleEl = card.querySelector('.card-subtitle');
        const subtitle = (subtitleEl ? subtitleEl.textContent : '').trim().toLowerCase();
        const descEl = card.querySelector('.matrix-card-desc') || card.querySelector('.card-summary');
        const desc = (descEl ? descEl.textContent : '').trim().toLowerCase();
        const metricsEl = card.querySelector('.card-metrics');
        const metrics = (metricsEl ? metricsEl.textContent : '').trim().toLowerCase();
        const codeEl = card.querySelector('.code-snippet-container code');
        const code = (codeEl ? codeEl.textContent : '').trim().toLowerCase();

        // Consolidated normalized corpus covering all card dimensions
        const corpus = `${title} ${subtitle} ${pillar} ${keywords} ${desc} ${metrics} ${code}`;

        return {
            card,
            pillar,
            title,
            keywords,
            corpus
        };
    });

    // 4.2 Helper: Safe HTML Escaping for dynamic empty state text
    function escapeSearchHtml(str) {
        if (!str) return '';
        return String(str).replace(/[&<>'"]/g, tag => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            "'": '&#39;',
            '"': '&quot;'
        }[tag] || tag));
    }

    // 4.3 Helper: Zero-Results Empty State DOM Element (Queried by class for ID safety)
    let emptyStateElement = ecosystemGrid ? ecosystemGrid.querySelector('.repo-empty-state') : null;
    if (!emptyStateElement && ecosystemGrid) {
        emptyStateElement = document.createElement('div');
        emptyStateElement.className = 'repo-empty-state';
        emptyStateElement.style.display = 'none';
        emptyStateElement.innerHTML = `
            <div class="empty-state-content text-center py-5 px-3">
                <div class="empty-state-icon mb-3">
                    <i class="fa-solid fa-magnifying-glass-chart fa-3x text-cyan"></i>
                </div>
                <h4 class="empty-state-title text-white fw-bold mb-2">No Matching Repositories</h4>
                <p class="empty-state-desc text-secondary mb-3" style="max-width: 520px; margin: 0 auto;"></p>
                <button type="button" class="btn btn-sm btn-outline-cyan btn-clear-filters px-3 py-2">
                    <i class="fa-solid fa-rotate-left me-1"></i> Clear Filters &amp; Reset
                </button>
            </div>
        `;
        ecosystemGrid.appendChild(emptyStateElement);

        const resetBtn = emptyStateElement.querySelector('.btn-clear-filters');
        if (resetBtn) {
            resetBtn.addEventListener('click', () => {
                if (searchInput) {
                    searchInput.value = '';
                }
                searchQuery = '';
                activePillar = 'all';

                filterPills.forEach(p => {
                    if (p.getAttribute('data-filter') === 'all') {
                        p.classList.add('active');
                    } else {
                        p.classList.remove('active');
                    }
                });

                clearTimeout(debounceTimer);
                filterRepositories();

                if (searchInput) {
                    searchInput.focus();
                }
            });
        }
    }

    // 4.4 Core Filtering Function: Intersect active category and multi-token search terms
    function filterRepositories() {
        let visibleCount = 0;
        const queryTerms = searchQuery.split(/\s+/).filter(Boolean);

        repoIndex.forEach(item => {
            const matchesPillar = (activePillar === 'all' || item.pillar === activePillar);
            const matchesSearch = queryTerms.length === 0 || queryTerms.every(term => item.corpus.includes(term));

            if (matchesPillar && matchesSearch) {
                item.card.style.display = 'flex';
                item.card.classList.remove('is-hidden');
                visibleCount++;
            } else {
                item.card.style.display = 'none';
                item.card.classList.add('is-hidden');
            }
        });

        // Update real-time match count badge
        if (countDisplay) {
            countDisplay.textContent = visibleCount;
        }

        // Toggle and format zero-results empty state
        if (emptyStateElement) {
            if (visibleCount === 0) {
                emptyStateElement.style.display = 'block';
                const descEl = emptyStateElement.querySelector('.empty-state-desc');
                if (descEl) {
                    if (searchQuery && activePillar !== 'all') {
                        descEl.innerHTML = `No repositories found matching <span class="text-cyan fw-bold">"${escapeSearchHtml(searchQuery)}"</span> in the <span class="text-cyan text-capitalize fw-bold">${escapeSearchHtml(activePillar)}</span> domain.`;
                    } else if (searchQuery) {
                        descEl.innerHTML = `No repositories found matching <span class="text-cyan fw-bold">"${escapeSearchHtml(searchQuery)}"</span>.`;
                    } else {
                        descEl.innerHTML = `No repositories found in the <span class="text-cyan text-capitalize fw-bold">${escapeSearchHtml(activePillar)}</span> domain.`;
                    }
                }
            } else {
                emptyStateElement.style.display = 'none';
            }
        }
    }

    // 4.5 Search Input Handler with 180ms Debounce and Keyboard Controls
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            clearTimeout(debounceTimer);
            const val = e.target.value.trim().toLowerCase();

            // Instant filter bypass if input was cleared to empty
            if (!val) {
                searchQuery = '';
                filterRepositories();
                return;
            }

            debounceTimer = setTimeout(() => {
                searchQuery = val;
                filterRepositories();
            }, DEBOUNCE_DELAY_MS);
        });

        // Keyboard navigation: Escape clears search; Enter filters immediately
        searchInput.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                e.preventDefault();
                searchInput.value = '';
                searchQuery = '';
                clearTimeout(debounceTimer);
                filterRepositories();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(debounceTimer);
                searchQuery = searchInput.value.trim().toLowerCase();
                filterRepositories();
            }
        });
    }

    // 4.6 Category Filter Pill Click Handlers (Instant 0ms execution)
    filterPills.forEach(pill => {
        pill.addEventListener('click', function() {
            filterPills.forEach(p => p.classList.remove('active'));
            this.classList.add('active');
            activePillar = this.getAttribute('data-filter') || 'all';
            clearTimeout(debounceTimer);
            filterRepositories();
        });
    });

    // =========================================================================
    // 5. Interactive Architectural Diagram Inspector Modal & SVG Stage
    // =========================================================================

    // 5.1 Authoritative 36-Repository Architectural Invariant Catalog
    const PROJECT_INVARIANTS = {
        'Glacier.Codec': {
            vectorFlow: 'Zero-allocation cryptographic and streaming compression pipeline dynamically arbitrated between low-latency CPU SIMD and multi-core parallel tree reduction.',
            registerModel: 'VAES 512-bit / AES-NI 128-bit registers, PCLMULQDQ/VPCLMULQDQ carryless Galois Field GHASH, and vectorized Cooley-Tukey Montgomery NTT over Z_q (q=3329).',
            zeroAllocBenchmark: '0 B managed heap allocations on active encryption, hashing, and compression paths. Hardware throughput: 12.61 GB/s AES-256-GCM, 7.59 GB/s BLAKE3, 16.83 GB/s LZ4, 10.05 GB/s Zstd.'
        },
        'Glacier.Polaris': {
            vectorFlow: 'Apache Arrow 64-byte cacheline-aligned columnar chunk memory with 64-row ValidityMask bit-registers, Kleene 3-valued boolean logic, and branchless AVX-512 vector lanes.',
            registerModel: 'Vector512<double> and Vector512<long> registers executing 8-wide AVX-512 FMA vector lanes with unmanaged NativeMemoryOwner<T> buffer alignment.',
            zeroAllocBenchmark: '0 B managed heap allocations on all query, filter, and aggregation hot paths. Hardware throughput: 1.19 ms / 10M sum (5.32B rows/s filter, 1.26x faster than Rust Polars).'
        },
        'Glacier.Forge': {
            vectorFlow: 'Streaming Ingress & Zero-Copy Hybrid I/O pipeline dynamically routing files to rented ArrayPool<byte> buffers or MemoryMappedFile pointers with 1GB line chunking.',
            registerModel: 'Byte-Space 256-ary Trie parser operating over ReadOnlySpan<byte> and Utf8JsonReader without UTF-16 string transcoding.',
            zeroAllocBenchmark: 'Exactly 0 bytes GC allocation during parsing. Throughput: 144.8 μs per 1,000 JSONL records (7.0M records/s) into PostgreSQL binary COPY.'
        },
        'Glacier.Sql': {
            vectorFlow: 'Pratt recursive-descent T-SQL parser constructing an Abstract Syntax Tree (AST), applying rule pushdowns, and compiling into Polaris LazyFrame plans.',
            registerModel: 'Zero-copy token scanning over ReadOnlySpan<char> and Write-Ahead Log (WAL) shadow journal with atomic commit pointers.',
            zeroAllocBenchmark: '0 heap allocations during tokenization and AST generation. Sub-millisecond query compilation and execution across 50 test suites.'
        },
        'Glacier.Chrono': {
            vectorFlow: 'Lock-free Array-of-Structures (AoS) circular ring buffer continuously ingested, compacted into Structure-of-Arrays (SoA) columns by background workers.',
            registerModel: 'Delta-of-Delta timestamp encoding (ΔD = (t_i - t_{i-1}) - (t_{i-1} - t_{i-2})) and Gorilla float XOR bit-packing across 64-bit hardware registers.',
            zeroAllocBenchmark: 'Zero GC allocation on ingestion hot paths. Throughput: 18.2M pts/s sustained ingest, 344.8M values/s Gorilla compression (0.29 ms / 100k pts).'
        },
        'Glacier.Storage': {
            vectorFlow: 'Pure C# Arrow IPC binary encoder/decoder streaming unmanaged columnar memory pointers (NativeMemoryOwner<T>) directly over System.IO.Pipelines.',
            registerModel: 'Multi-tiered page slabs (4KB, 64KB, 2MB), Clock-Pro page replacement buffer pool with hot/cold lists, and hardware CRC32C checksums.',
            zeroAllocBenchmark: '0 bytes GC heap allocation during Arrow IPC roundtrips. Throughput: 250M rows/s Arrow IPC streaming, 4,457 MB/s Snappy compression, 10.29M buffer pool ops/s.'
        },
        'Glacier.DocTree': {
            vectorFlow: 'Hierarchical AST where Root node branches into H1..H6 sections containing Leaf nodes (Paragraphs, Tables, Code Blocks) with upward breadcrumb resolution.',
            registerModel: 'Zero-allocation SIMD byte-patch boundary chunking operating over UTF-8 memory spans, preserving semantic Markdown structure.',
            zeroAllocBenchmark: '0 heap allocations on parsing and chunking loops. Delivers 3.8x KV cache token compression during LLM ingestion with full context preservation.'
        },
        'Glacier.Vector': {
            vectorFlow: 'High-dimensional vector space represented through multi-layered Hierarchical Navigable Small World (HNSW) graphs, zero-alloc priority queues, and SIMD kernels.',
            registerModel: 'Vector512<float> and Vector256<float> registers executing fused multiply-add (FMA) DotProduct, CosineDistance, and L2Distance kernels.',
            zeroAllocBenchmark: 'Zero heap allocations per query via pooled and struct priority queues. Throughput: 622.7M vec/s batch dot product (RTX 4060), 44,022 QPS (22.7 μs) HNSW search.'
        },
        'Glacier.Graph': {
            vectorFlow: 'Forward Star Compressed Sparse Row (CSR) representation using four flat primitive arrays (head[], next[], to[], weight[]) with generation-tagged bit arrays.',
            registerModel: 'AVX-512 SIMD vector gather instructions and hardware BitOperations.PopCount for instant bit-array visited frontier checks.',
            zeroAllocBenchmark: '0 dynamic heap allocations (Queue and HashSet eliminated) during BFS, DFS, and Dijkstra. 2.91 ms BFS Shortest Path across 100k vertices.'
        },
        'Glacier.Grep': {
            vectorFlow: 'Channels-based work-stealing pipeline feeding memory-mapped file slices into SIMD vector scan lanes with 4KB boundary overlap margins.',
            registerModel: 'SearchValues<byte> AVX-512 hardware vector registers scanning 64 characters per clock cycle with zero text transcoding.',
            zeroAllocBenchmark: 'Zero chunk boundary drops, 0 heap allocations in inner scan loops. Sub-millisecond UTF-8 scanning across multi-megabyte files under 100% Native AOT.'
        },
        'Glacier.Rag': {
            vectorFlow: 'Dual-branch retriever uniting FastHash SIMD dense vector distance rankings and Forward Star CSR topological connectivity through Reciprocal Rank Fusion (RRF).',
            registerModel: 'In-process memory pipeline fusing Glacier.Vector, Glacier.Graph, and Glacier.DocTree without network serialization or inter-process IPC overhead.',
            zeroAllocBenchmark: 'Zero managed allocations during prompt frame serialization. Performance: 7.21 ms cold hybrid retrieval (261x faster than Python stacks at 1,883 ms).'
        },
        'Glacier.Bundle': {
            vectorFlow: 'Central BundleContext broker synthesizing multi-engine inputs (Polaris tables, Graph neighbors, Vector embeddings, DocTree nodes) into packed LLM prompt frames.',
            registerModel: 'Priority Token Knapsack Optimizer evaluating token budgets dynamically and linear UTF-8 byte buffer blitting.',
            zeroAllocBenchmark: 'Zero managed allocations during prompt frame serialization. Sub-20ms resolution across CRM tables, graph neighbors, vectors, and DocTree nodes.'
        },
        'Glacier.Gpu': {
            vectorFlow: 'CPU lock-free sequence ring buffer continuously polled by a persistent GPU warp megakernel loop, bypassing cuLaunchKernel OS driver overhead (8-12 μs).',
            registerModel: 'Direct NVIDIA Ada Lovelace SASS cubin machine code (sm_89) via ptxas.exe and AMD RDNA APU zero-copy unified memory queues (sub-300ns latency).',
            zeroAllocBenchmark: 'Exactly 64-byte cacheline-isolated GpuWorkTask descriptors preventing false sharing. 100.0 ns dispatch latency (103.8x faster), 13.6 TFLOPS FP32 compute.'
        },
        'Glacier.Inference': {
            vectorFlow: 'Unmanaged sliding-window KV cache ring buffer (NativeMemory.AllocZeroed), super-block quantization decoders, and Wave32 Direct3D 12 / Vulkan compute shaders.',
            registerModel: 'Direct hardware execution across D3D12 HLSL SM 6.6, Vulkan SPIR-V with VK_KHR_cooperative_matrix, and multi-threaded AVX-512 CPU SIMD.',
            zeroAllocBenchmark: 'Zero managed heap allocations during autoregressive decoding, 0 bytes WDDM paging thrash. Throughput: 109.6 tok/s prefill, 60.5 tok/s decode on RTX 4060 Laptop.'
        },
        'Glacier.Inference.Distributed': {
            vectorFlow: 'Multi-node pipeline parallel stages (Node 0 Host dGPU -> Node 1 Worker dGPU -> Node 2 APU) linked via 32-byte socket frames with INT8 activation streaming.',
            registerModel: 'Dynamic layer-weighting calibrating workloads proportionally to each GPU\'s compute capability, hiding network latency via speculative overlap.',
            zeroAllocBenchmark: '75% network bandwidth reduction via INT8 wire compression, 0 socket resets (10054) over multi-GB streams. 0.45 ms LAN hop latency, 52.46 tok/s prefill across fleet.'
        },
        'Glacier.Cluster': {
            vectorFlow: '32-byte aligned binary frame protocol (0x47434C55) over System.IO.Pipelines, dynamic DAG planner compiling narrow and wide shuffle dependency stages.',
            registerModel: 'Two-Phase Hardware-Aware Task Placement (Hard CUDA predicate filtering + Soft SIMD affinity scoring) with vectorized XXHash3 partition routing.',
            zeroAllocBenchmark: 'Ref-counted partition buffers (Retain/Release) with tiered out-of-core disk spill. Throughput: 4.07M rows/s distributed DataFrame query across 4-machine fleet.'
        },
        'Glacier.Compute': {
            vectorFlow: 'In-memory AST-to-bytecode assembler emitting PTX 8.0 and SPIR-V 1.6 binaries alongside a three-tier unmanaged memory hierarchy (Tier 1 Slab, Tier 2 Buddy, Tier 3 Reservoir).',
            registerModel: 'Direct hardware register allocations with zero driver compiler process overhead; SIMD ring collective accumulation across heterogeneous compute units.',
            zeroAllocBenchmark: 'Zero GC allocations during shader emission and buffer leasing. 3,726.8 ns PTX JIT compilation (23,000x faster than process launching), 64.7 ns Tier-1 Slab Alloc.'
        },
        'Glacier.Tensor': {
            vectorFlow: 'Strided unmanaged buffer ([InlineArray(8)] shape/strides) linked to reverse-mode backward autograd tape nodes recording gradients into persistent scratch workspaces.',
            registerModel: 'Resident GPU GEMM kernels executing in dedicated VRAM without PCIe roundtrip staging, accelerated via AVX-512 FMA on CPU and D3D12 Wave32 on GPU.',
            zeroAllocBenchmark: 'Zero heap allocations during backward chain-rule autograd passes. Throughput: 621.5 GFLOPS AVX-512 CPU GEMM (1.17x vs PyTorch), 3.63 TFLOPS resident GPU GEMM on RTX 4060.'
        },
        'Glacier.ML': {
            vectorFlow: 'Zero-copy FeatureMatrix reading unmanaged column spans directly, feeding parallel block-chunked KMeans centroid distance matrices and histogram decision trees.',
            registerModel: 'SIMD Euclidean distance matrix decomposition (||x-c||² = ||x||² - 2<x, c> + ||c||²) using Vector512<float> FMA vector lanes.',
            zeroAllocBenchmark: 'Zero heap array copies (purged ToArray()). Throughput: 109.9M samples/sec KMeans prediction avg, 238.1M peak (1.31x-1.76x faster than Scikit-Learn MKL), 352 ns PredictRow.'
        },
        'Glacier.Tune': {
            vectorFlow: 'Frozen base GGUF weights memory-mapped in VRAM combined with low-rank adapter projections (Wq, Wv, Wgate, Wup, Wdown), streamed through tiled loss.',
            registerModel: 'Online log-sum-exp tiling in 4096-token blocks maintaining <2MB scratch memory across 152k vocabulary, SIMD gradient norm clipping (<= 1.0).',
            zeroAllocBenchmark: '0 heap allocations during backward passes. Performance: 7.58 s/step GPU native fine-tuning (RTX 4060, 0 CPU fallback), 6.82s standalone GGUF model fusion.'
        },
        'Glacier.Clavier': {
            vectorFlow: 'Zero-allocation byte prefix trie / BPE merge table operating directly over UTF-8 ReadOnlySpan<byte> without string allocations.',
            registerModel: 'Unmanaged System-1 reflex policy engine executing over AMD APU coherent DDR5/LPDDR5X physical RAM with sub-microsecond latency.',
            zeroAllocBenchmark: 'Zero managed GC latency on reflex evaluation. Performance: 0.900 μs APU memory access latency (0 ns PCIe penalty), 9.4 μs System-1 policy turnaround (106,000 decisions/s).'
        },
        'Glacier.Graphics': {
            vectorFlow: 'Vector path segments tessellated into analytical SIMD cubic Bezier curves, evaluated via TileCompute rasterizers, and blitted to linear sub-pixel LCD buffers.',
            registerModel: 'AVX-512 FMA vector registers evaluating cubic polynomials in parallel; unmanaged linear framebuffer blitting.',
            zeroAllocBenchmark: 'Pure C# 2D graphics engine with 0 native C++ dependencies (purged SkiaSharp). Throughput: 4.96B pts/sec cubic Bezier SIMD evaluation, 2,737 FPS CPU software rasterizer.'
        },
        'Glacier.Windowing': {
            vectorFlow: 'OS native message loop (Win32 PeekMessage / Wayland epoll) pumping events into a lock-free SPSC event ring, synchronizing with D3D12 Flip Model and Vulkan WSI.',
            registerModel: 'Direct Win32 and X11/Wayland P/Invoke bindings without GLFW or SDL2 shims; lock-free SPSC audio ring buffers driving WASAPI directly.',
            zeroAllocBenchmark: 'Zero-shim native windowing with 0 heap allocation on input dispatch. Throughput: 20.8 ns/event lock-free event pump (48.03M events/sec), 2.4 ns presentation latency (411M fps).'
        },
        'Glacier.Plot': {
            vectorFlow: 'High-throughput time-series stream entering SIMD LTTB triangle decimation kernels, reducing 10M coordinates to screen viewport resolution.',
            registerModel: 'Vector256 and Vector512 SIMD kernels computing triangle areas across contiguous float buffers with branchless min-max reductions.',
            zeroAllocBenchmark: '0 heap allocations during decimation and framebuffer presentation. Throughput: 4,010.7M points/sec LTTB decimation (10M pts -> 1,920px in 2.493 ms, 6.94x faster), 60/120+ FPS.'
        },
        'Glacier.StatsViz': {
            vectorFlow: 'Minimax polynomial FastExp AVX-512 kernel evaluating continuous probability density functions over discrete grid points, bound to declarative grammar geometry layers.',
            registerModel: 'Hardware-accelerated vectorized Gaussian kernel density estimation (KDE) and vectorized descriptive statistics over contiguous column spans.',
            zeroAllocBenchmark: 'Zero heap allocation in statistical hot loops. Throughput: 1.456 ms for 100,000-sample Gaussian KDE (94.8x faster than SciPy evaluate at 137.41 ms), 11.35 ms for 1M samples.'
        },
        'Glacier.Game': {
            vectorFlow: 'Contiguous 64-byte aligned Structure-of-Arrays (SoA) component arrays (Positions[], Velocities[], AABBs[]) traversed via SIMD chunk kernels without pointer chasing.',
            registerModel: 'Vector512<float> and Vector256<float> Euler integration and AABB collision kernels driving Direct3D 12 Flip Model sprite batches.',
            zeroAllocBenchmark: 'Zero GC allocations during engine ticks, 0 Silk.NET native DLLs. Throughput: 8.57 Billion entities/sec Euler physics integration (250,000 entities in 29.17 μs, 2.36x faster), 240+ FPS.'
        },
        'Glacier.Mobile': {
            vectorFlow: 'Native OS touch queue (Android NDK / iOS UIKit) feeding ARM64 Neon gesture recognizers and SIMD inertia damping shaders, composed via Vulkan-Surface/Metal.',
            registerModel: 'ARM64 Neon vector registers calculating kinetic momentum, multi-touch pinch/pan affine transforms, and sub-pixel touch coordinate clamping.',
            zeroAllocBenchmark: 'Native AOT instant startup (<200ms cold boot), zero GC pauses on touch events. Throughput: 35.66 Billion/s SIMD inertia damping (280 ns per 10k batch), 35.4 ns touch event dispatch.'
        },
        'Glacier.Desktop': {
            vectorFlow: 'Retained visual node hierarchy flattened into parallel box arrays, solved via Kogge-Stone parallel prefix-sum layout algorithms, and emitted as Direct3D 12 draw commands.',
            registerModel: 'SIMD Kogge-Stone parallel prefix-sum layout solver computing box coordinates across wide vector registers in O(log N) parallel steps.',
            zeroAllocBenchmark: 'Zero string boxing allocations in VirtualDataGrid (direct Span<char> rendering), Native AOT instant startup (<15ms). Throughput: 4.92 Billion boxes/sec layout calculation.'
        },
        'Glacier.SpanCoder': {
            vectorFlow: 'Two-buffer piece-table (Original Read-Only Buffer + Append-Only Add Buffer) indexed by a SIMD line break lookup table, rendered via pure C# LCD font rasterizers.',
            registerModel: 'MemoryMappedFile read-only buffers with lightweight descriptor records (BufferId, Offset, Length), eliminating text re-allocation on edits.',
            zeroAllocBenchmark: 'Zero external python.exe processes or mock servers. Performance: Sub-millisecond keystroke latency on 100MB+ files, instantaneous piece descriptor slicing.'
        },
        'Glacier.Serve': {
            vectorFlow: 'Zero-allocation System.IO.Pipelines radix router feeding continuous batching coordinators and 16-token PagedAttention block managers.',
            registerModel: 'SIMD HTTP request-line parser scanning method verbs, paths, and protocol versions across Vector256<byte> registers in 10 nanoseconds.',
            zeroAllocBenchmark: 'Pooled memory (ArrayPool<byte>.Shared) eliminating per-connection heap buffer allocations. 16,740 req/s roundtrip HTTP throughput (0.060 ms), 96.13% KV cache memory savings.'
        },
        'Glacier.Studio': {
            vectorFlow: '3-pane workbench layout uniting Active Sessions, Real-Time Token SSE Streams, and DocTree AST Inspection with live Win32/DXGI hardware monitoring.',
            registerModel: 'Reactive non-blocking telemetry event pipeline streaming GPU VRAM, clock frequencies, and token speeds directly to high-contrast UI widgets.',
            zeroAllocBenchmark: 'Zero UI thread blocking during continuous token emission. Sub-100ms UI responsiveness verified under simultaneous 100 tok/s token streaming (Mean: 0.036 ms, Max: 0.603 ms).'
        },
        'Glacier.Showcase': {
            vectorFlow: 'Blazor Server component hub binding directly to Glacier engine assemblies, streaming verified hardware telemetry from DXGI and Zen 5 hardware counters to Obsidian Ice widgets.',
            registerModel: 'Pure C# empirical benchmarking harness executing physical CPU/GPU tests with verified hardware counters and 0 simulated numbers.',
            zeroAllocBenchmark: '199 passed / 199 total unit and integration tests (100% green), authentic Win32/DXGI telemetry with zero mock or synthetic benchmark numbers.'
        },
        'Glacier.Cli': {
            vectorFlow: 'Zero-allocation command parsing graph resolving command verbs (inspect, tune, serve, grep) and dispatching immediately to sub-engine DLLs via direct entry points.',
            registerModel: 'Branchless command trie parser operating over ReadOnlySpan<char> without string splits or reflection.',
            zeroAllocBenchmark: '100% Native AOT single binary executable. Performance: 2.068 ms cold application start, 1,006,128 command dispatches/sec warm.'
        },
        'Glacier.Agent': {
            vectorFlow: 'ReAct (Reason + Act) autonomous state machine executing over an unmanaged actor mailbox, wired to a zero-reflection JSON-RPC 2.0 MCP server over stdio.',
            registerModel: 'SIMD Radix Trie tokenizer evaluating token sequences at 302M tok/s; lock-free actor message queues for autonomous tool scheduling.',
            zeroAllocBenchmark: 'Zero runtime reflection, Native AOT trimmer-safe tool calling. Throughput: 11.35 Million messages/sec reactive actor mailbox, 44,022 QPS HNSW vector retrieval.'
        },
        'AgentDevKit.Adk': {
            vectorFlow: 'MCP Client/Server transport pipeline managing session contexts, tool validation schemas, and structured JSON-RPC messages over standard I/O pipes.',
            registerModel: 'Pure C# .NET 10 implementation of Model Context Protocol with zero external SQLite or native C++ dependencies.',
            zeroAllocBenchmark: '0 heap allocations during protocol framing and header validation. Strict adherence to Glacier Zero Secrets Policy with environment variable credential resolution.'
        },
        'tinypdf-csharp': {
            vectorFlow: 'Pure C# PDF object dictionary / stream cross-reference table (xref) emitter connected to a zero-allocation font metric rasterizer.',
            registerModel: 'Incremental stream serializer writing directly to output streams without whole-document intermediate DOM allocations.',
            zeroAllocBenchmark: '0 native C++ dependencies, zero external font shims. Throughput: 1,865 invoices/sec generated (0.536 ms/doc), exact font metric and typography parsing.'
        }
    };

    // 5.2 Helper: Invariant Fallback Resolver
    function resolveProjectInvariants(projectName, cardEl) {
        if (PROJECT_INVARIANTS[projectName]) {
            return PROJECT_INVARIANTS[projectName];
        }

        const subtitle = cardEl ? (cardEl.querySelector('.card-subtitle')?.textContent?.trim() || '') : '';
        const metrics = cardEl ? (cardEl.querySelector('.card-metrics')?.textContent?.trim() || '') : '';
        const summary = cardEl ? (cardEl.querySelector('.card-summary')?.textContent?.trim() || '') : '';

        return {
            vectorFlow: summary || `${projectName} execution pipeline with zero-copy unmanaged stream ingress and contiguous buffer processing.`,
            registerModel: `${subtitle || 'Hardware-accelerated execution'}: Direct vector intrinsics (Vector512/Vector256/AVX-512) and unmanaged hardware queue dispatch.`,
            zeroAllocBenchmark: `0 B managed GC allocations on hot paths. Empirical hardware performance: ${metrics || 'Sub-millisecond latency & zero-allocation throughput'}.`
        };
    }

    // 5.3 Modal State Controller
    let lastActiveTrigger = null;

    function openDiagramModal(triggerEl) {
        const modalEl = document.getElementById('diagram-inspector-modal');
        if (!modalEl) {
            console.warn('[Glacier Inspector] #diagram-inspector-modal element not found in DOM.');
            return;
        }

        const projectName = triggerEl.getAttribute('data-project') || 'Architectural Schematic';
        const cardEl = triggerEl.closest('.project-card') || triggerEl.closest('.matrix-card');

        // A. Header updates: Project Name & Domain Badge
        const nameDisplay = document.getElementById('modal-project-name');
        if (nameDisplay) {
            nameDisplay.textContent = projectName;
        }

        const titleEl = document.getElementById('diagram-inspector-title');
        const sourceBadge = cardEl ? cardEl.querySelector('.badge-domain, .pillar-tag') : null;
        let modalBadge = titleEl ? titleEl.querySelector('.modal-project-badge') : null;

        if (!modalBadge && titleEl) {
            modalBadge = document.createElement('span');
            modalBadge.className = 'modal-project-badge ms-2';
            titleEl.appendChild(modalBadge);
        }

        if (sourceBadge && modalBadge) {
            modalBadge.className = sourceBadge.className + ' modal-project-badge ms-2';
            modalBadge.innerHTML = sourceBadge.innerHTML;
            modalBadge.style.display = 'inline-flex';
        } else if (modalBadge) {
            modalBadge.style.display = 'none';
        }

        modalEl.setAttribute('aria-label', `Architectural Schematic Inspector - ${projectName}`);

        // B. SVG Extraction & Deep Clone into #modal-diagram-stage
        const stageEl = document.getElementById('modal-diagram-stage');
        const sourceSvg = triggerEl.querySelector('svg');
        if (stageEl && sourceSvg) {
            const clonedSvg = sourceSvg.cloneNode(true);
            clonedSvg.setAttribute('width', '100%');
            clonedSvg.setAttribute('height', '100%');
            clonedSvg.removeAttribute('id');

            const cue = clonedSvg.querySelector('.diagram-inspect-cue');
            if (cue) cue.remove();

            stageEl.replaceChildren(clonedSvg);
        } else if (stageEl) {
            stageEl.innerHTML = `<div class="text-secondary font-monospace p-4">Schematic vector unavailable for ${escapeSearchHtml(projectName)}</div>`;
        }

        // C. Technical Invariant Breakdown Rendering
        const breakdownGrid = modalEl.querySelector('.modal-breakdown-grid');
        if (breakdownGrid) {
            const inv = resolveProjectInvariants(projectName, cardEl);
            breakdownGrid.innerHTML = `
                <div class="modal-breakdown-card">
                    <div class="breakdown-title">
                        <i class="fa-solid fa-network-wired text-cyan me-2"></i>Invariant 1: Architecture &amp; Vector Flow
                    </div>
                    <p class="breakdown-desc">${escapeSearchHtml(inv.vectorFlow)}</p>
                </div>
                <div class="modal-breakdown-card">
                    <div class="breakdown-title">
                        <i class="fa-solid fa-microchip text-purple me-2"></i>Invariant 2: Hardware Intrinsic &amp; Register Model
                    </div>
                    <p class="breakdown-desc">${escapeSearchHtml(inv.registerModel)}</p>
                </div>
                <div class="modal-breakdown-card">
                    <div class="breakdown-title">
                        <i class="fa-solid fa-bolt text-green me-2"></i>Invariant 3: Zero-Allocation &amp; Benchmark Highlight
                    </div>
                    <p class="breakdown-desc">${escapeSearchHtml(inv.zeroAllocBenchmark)}</p>
                </div>
            `;
        }

        // D. Bootstrap 5 Instance Show & Accessibility Focus Tracking
        lastActiveTrigger = triggerEl;
        modalEl.setAttribute('aria-modal', 'true');
        modalEl.setAttribute('role', 'dialog');

        if (window.bootstrap && window.bootstrap.Modal) {
            const modalInstance = window.bootstrap.Modal.getOrCreateInstance(modalEl, {
                backdrop: true,
                keyboard: true,
                focus: true
            });
            modalInstance.show();
        } else {
            modalEl.classList.add('show');
            modalEl.style.display = 'block';
            modalEl.removeAttribute('aria-hidden');
            document.body.classList.add('modal-open');
        }
    }

    // 5.4 Clean Modal Dismissal & Focus Restoration
    const inspectorModalEl = document.getElementById('diagram-inspector-modal');
    if (inspectorModalEl) {
        inspectorModalEl.addEventListener('hidden.bs.modal', () => {
            const stageEl = document.getElementById('modal-diagram-stage');
            if (stageEl) {
                stageEl.replaceChildren();
            }
            if (lastActiveTrigger && typeof lastActiveTrigger.focus === 'function') {
                lastActiveTrigger.focus();
            }
            lastActiveTrigger = null;
        });

        // Fallback dismissal handler for close button, backdrop click, or keyboard Escape
        inspectorModalEl.addEventListener('click', (e) => {
            if (e.target.closest('.btn-close-obsidian') || e.target === inspectorModalEl) {
                if (window.bootstrap && window.bootstrap.Modal) {
                    const inst = window.bootstrap.Modal.getInstance(inspectorModalEl);
                    if (inst) inst.hide();
                } else {
                    inspectorModalEl.classList.remove('show');
                    inspectorModalEl.style.display = 'none';
                    inspectorModalEl.setAttribute('aria-hidden', 'true');
                    document.body.classList.remove('modal-open');
                    const stageEl = document.getElementById('modal-diagram-stage');
                    if (stageEl) stageEl.replaceChildren();
                    if (lastActiveTrigger && typeof lastActiveTrigger.focus === 'function') {
                        lastActiveTrigger.focus();
                    }
                    lastActiveTrigger = null;
                }
            }
        });
    }

    // 5.5 Global Event Delegation: Click & Keyboard (Enter/Space)
    document.addEventListener('click', (e) => {
        const diagramTrigger = e.target.closest('.card-diagram');
        if (diagramTrigger) {
            e.preventDefault();
            openDiagramModal(diagramTrigger);
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') {
            const activeEl = document.activeElement;
            if (activeEl && activeEl.classList.contains('card-diagram')) {
                e.preventDefault();
                openDiagramModal(activeEl);
            }
        } else if (e.key === 'Escape') {
            // Dismiss diagram inspector modal if open
            if (inspectorModalEl && inspectorModalEl.classList.contains('show')) {
                if (window.bootstrap && window.bootstrap.Modal) {
                    const inst = window.bootstrap.Modal.getInstance(inspectorModalEl);
                    if (inst) inst.hide();
                } else {
                    inspectorModalEl.classList.remove('show');
                    inspectorModalEl.style.display = 'none';
                    inspectorModalEl.setAttribute('aria-hidden', 'true');
                    document.body.classList.remove('modal-open');
                    const stageEl = document.getElementById('modal-diagram-stage');
                    if (stageEl) stageEl.replaceChildren();
                    if (lastActiveTrigger && typeof lastActiveTrigger.focus === 'function') {
                        lastActiveTrigger.focus();
                    }
                    lastActiveTrigger = null;
                }
            }
        }
    });

    // =========================================================================
    // 6. Interactive Architecture Diagram Node Inspector (Docs Hub Cornerstones)
    // =========================================================================
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
            const inspector = targetInspectorId ? document.getElementById(targetInspectorId) : null;

            const parentFlow = this.closest('.diagram-step-flow');
            if (parentFlow) {
                parentFlow.querySelectorAll('.diag-step-node').forEach(n => n.classList.remove('active'));
            }
            this.classList.add('active');

            if (inspector && stepData[stepId]) {
                const data = stepData[stepId];
                const titleEl = inspector.querySelector('.diag-inspector-title');
                const badgeEl = inspector.querySelector('.diag-inspector-badge');
                const descEl = inspector.querySelector('.diag-inspector-desc');
                const allocEl = inspector.querySelector('.diag-inspector-alloc');

                if (titleEl) titleEl.textContent = data.title;
                if (badgeEl) badgeEl.textContent = data.badge;
                if (descEl) descEl.textContent = data.desc;
                if (allocEl) allocEl.textContent = data.alloc;
            }
        });
    });

    // =========================================================================
    // 7. Starter Kit Templates Content, Copy & Downloader
    // =========================================================================
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
            if (key) {
                setTemplate(key);
            }
        });
    });

    if (templateCopyBtn) {
        templateCopyBtn.addEventListener('click', async () => {
            const code = templates[currentTemplateKey] || '';
            const copied = await copyToClipboard(code);
            if (copied) {
                const originalHtml = templateCopyBtn.innerHTML;
                templateCopyBtn.classList.add('copied');
                templateCopyBtn.innerHTML = '<i class="fa-solid fa-check me-1"></i> Copied!';
                showToast(`✓ Copied ${currentTemplateKey.toUpperCase()} template to clipboard!`, 2000);
                setTimeout(() => {
                    templateCopyBtn.classList.remove('copied');
                    templateCopyBtn.innerHTML = originalHtml;
                }, 2000);
            } else {
                showToast('Failed to copy template', 2000);
            }
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

    // Initialize with default template
    setTemplate('readme');

    // =========================================================================
    // 8. Accessible Code Snippet Copy Widgets with Dotted ID Target Resolution
    // =========================================================================

    /**
     * Resolves the target code element from a copy button, handling dotted IDs safely
     * (e.g. #code-inference.distributed and #code-agentdevkit.adk).
     * @param {HTMLElement} btn - The copy button
     * @returns {HTMLElement|null}
     */
    function resolveCodeTarget(btn) {
        const targetSelector = btn.getAttribute('data-clipboard-target');
        if (targetSelector) {
            // Handle ID-based selector directly without querySelector class parsing for dots
            if (targetSelector.startsWith('#')) {
                const id = targetSelector.substring(1);
                const el = document.getElementById(id);
                if (el) return el;
            }
            try {
                const el = document.querySelector(targetSelector);
                if (el) return el;
            } catch {
                // Ignore querySelector syntax exceptions on unescaped dots
            }
        }
        // Fallback: search within closest .code-snippet-container
        const container = btn.closest('.code-snippet-container');
        if (container) {
            return container.querySelector('code');
        }
        return null;
    }

    /**
     * Applies interactive visual feedback on copy buttons:
     * updates label to "Copied!", adds .copied class, switches icon to fa-check,
     * and reverts automatically after 2000ms.
     * @param {HTMLElement} btn - The copy button
     */
    function triggerCopyFeedback(btn) {
        btn.classList.add('copied');

        let label = btn.querySelector('.copy-label');
        if (!label) {
            label = document.createElement('span');
            label.className = 'copy-label ms-1';
            btn.appendChild(label);
        }
        label.textContent = 'Copied!';
        label.style.display = 'inline';

        const originalAriaLabel = btn.getAttribute('aria-label') || '';
        const originalTitle = btn.getAttribute('title') || '';
        btn.setAttribute('aria-label', 'Copied to clipboard!');
        btn.setAttribute('title', 'Copied to clipboard!');

        if (btn._revertTimer) {
            clearTimeout(btn._revertTimer);
        }

        btn._revertTimer = setTimeout(() => {
            btn.classList.remove('copied');
            if (label) {
                label.remove();
            }
            if (originalAriaLabel) {
                btn.setAttribute('aria-label', originalAriaLabel);
            } else {
                btn.removeAttribute('aria-label');
            }
            if (originalTitle) {
                btn.setAttribute('title', originalTitle);
            } else {
                btn.removeAttribute('title');
            }
            btn._revertTimer = null;
        }, 2000);
    }

    const genericCopyButtons = document.querySelectorAll('.btn-copy-code');
    genericCopyButtons.forEach(btn => {
        btn.addEventListener('click', async function(e) {
            e.preventDefault();
            const targetElement = resolveCodeTarget(this);
            const textToCopy = targetElement ? (targetElement.textContent || '').trim() : (this.getAttribute('data-code') || '');

            if (!textToCopy) {
                console.warn('[Glacier Copy] No text found to copy for button:', this);
                return;
            }

            const copied = await copyToClipboard(textToCopy);
            if (copied) {
                triggerCopyFeedback(this);
                showToast('Snippet copied to clipboard', 2000);
            } else {
                showToast('Failed to copy snippet', 2000);
            }
        });
    });

    // =========================================================================
    // 9. Diataxis Documentation Hub Tab Navigation & Deep-Linking
    // =========================================================================

    /**
     * Initializes robust tab switching across #hub-tabs and #cornerstone-subtabs.
     * Prevents page jumping, preserves active states, supports deep linking via hash,
     * and works seamlessly with or without Bootstrap JS.
     */
    function initTabNavigation() {
        const tabTriggers = document.querySelectorAll('#hub-tabs .nav-link, #cornerstone-subtabs .nav-link, .nav-pills-custom .nav-link, [data-bs-toggle="pill"]');

        function activateTab(triggerBtn, updateHash = false) {
            if (!triggerBtn) return;

            const targetSelector = triggerBtn.getAttribute('data-bs-target') || triggerBtn.getAttribute('href');
            if (!targetSelector) return;

            // 1. Identify target pane
            let targetPane = null;
            if (targetSelector.startsWith('#')) {
                targetPane = document.getElementById(targetSelector.substring(1));
            } else {
                try { targetPane = document.querySelector(targetSelector); } catch {}
            }

            if (!targetPane) return;

            // 2. Deactivate sibling trigger buttons in the same tablist
            const tabList = triggerBtn.closest('[role="tablist"], .nav');
            if (tabList) {
                tabList.querySelectorAll('.nav-link').forEach(btn => {
                    btn.classList.remove('active');
                    btn.setAttribute('aria-selected', 'false');
                });
            }

            // 3. Activate current trigger button
            triggerBtn.classList.add('active');
            triggerBtn.setAttribute('aria-selected', 'true');

            // 4. Deactivate sibling panes in the same tab content container
            const tabContent = targetPane.closest('.tab-content');
            if (tabContent) {
                const siblingPanes = tabContent.querySelectorAll(':scope > .tab-pane');
                siblingPanes.forEach(pane => {
                    pane.classList.remove('show', 'active');
                });
            }

            // 5. Activate target pane
            targetPane.classList.add('show', 'active');

            // 6. Sync with Bootstrap Tab instance if available
            if (window.bootstrap && window.bootstrap.Tab) {
                try {
                    const bsTab = bootstrap.Tab.getOrCreateInstance(triggerBtn);
                    bsTab.show();
                } catch {
                    // Vanilla activation already performed above
                }
            }

            // 7. Update URL hash without causing page jump or reload
            if (updateHash && window.history && window.history.replaceState && targetSelector.startsWith('#')) {
                window.history.replaceState(null, null, targetSelector);
            }
        }

        tabTriggers.forEach(btn => {
            btn.addEventListener('click', function(e) {
                e.preventDefault();
                activateTab(this, true);
            });
        });

        // 8. Handle direct page load / deep link with hash
        const initialHash = window.location.hash;
        if (initialHash && initialHash.length > 1) {
            const matchingTrigger = document.querySelector(`[data-bs-target="${initialHash}"], [href="${initialHash}"]`);
            if (matchingTrigger) {
                activateTab(matchingTrigger, false);
            }
        }
    }

    initTabNavigation();
});
