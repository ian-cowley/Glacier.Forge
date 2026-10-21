using System;
using System.Buffers;
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Glacier.Forge;

/// <summary>
/// Delegate for zero-allocation processing of raw file data spans.
/// </summary>
public delegate void FileDataProcessor(ReadOnlySpan<byte> data);

/// <summary>
/// Hybrid I/O Dispatcher that routes I/O patterns based on file size
/// to bypass stream buffer overhead and utilize zero-copy memory mapping or pooled array blocks.
/// </summary>
public static class HybridIoDispatcher
{
    private const long OneMegaByte = 1024 * 1024;

    public static void ProcessFile(string path, long length, FileDataProcessor processor)
    {
        if (length <= 0)
        {
            processor([]);
            return;
        }

        if (length < OneMegaByte)
        {
            ProcessRentedArray(path, (int)length, processor);
        }
        else
        {
            ProcessMemoryMapped(path, processor);
        }
    }

    private static void ProcessRentedArray(string path, int length, FileDataProcessor processor)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                FileOptions.SequentialScan
            );

            int totalBytesRead = 0;
            while (totalBytesRead < length)
            {
                int bytesRead = RandomAccess.Read(handle, rented.AsSpan(totalBytesRead, length - totalBytesRead), totalBytesRead);
                if (bytesRead == 0) break;
                totalBytesRead += bytesRead;
            }
            processor(rented.AsSpan(0, totalBytesRead));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ProcessMemoryMapped(string path, FileDataProcessor processor)
    {
        using var mmf = MemoryMappedFile.CreateFromFile(
            path, 
            FileMode.Open, 
            null, 
            0, 
            MemoryMappedFileAccess.Read
        );
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        
        unsafe
        {
            byte* pointer = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                byte* start = pointer + accessor.PointerOffset;
                long capacity = accessor.Capacity;

                // ReadOnlySpan length is limited to int.MaxValue (2GB).
                // If capacity is within int.MaxValue, process in one contiguous span.
                if (capacity <= int.MaxValue)
                {
                    var fileData = new ReadOnlySpan<byte>(start, (int)capacity);
                    processor(fileData);
                }
                else
                {
                    const int maxSpanChunk = 1024 * 1024 * 1024; // 1 GB target block
                    long remaining = capacity;
                    long offset = 0;
                    while (remaining > 0)
                    {
                        if (remaining <= maxSpanChunk)
                        {
                            var fileData = new ReadOnlySpan<byte>(start + offset, (int)remaining);
                            processor(fileData);
                            break;
                        }

                        // Search backwards from maxSpanChunk for the last newline to preserve JSON lines
                        int candidateChunk = maxSpanChunk;
                        while (candidateChunk > 0 && *(start + offset + candidateChunk - 1) != (byte)'\n')
                        {
                            candidateChunk--;
                        }

                        if (candidateChunk == 0)
                        {
                            candidateChunk = maxSpanChunk;
                        }

                        var fileDataChunk = new ReadOnlySpan<byte>(start + offset, candidateChunk);
                        processor(fileDataChunk);
                        offset += candidateChunk;
                        remaining -= candidateChunk;
                    }
                }
            }
            finally
            {
                accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }
}
