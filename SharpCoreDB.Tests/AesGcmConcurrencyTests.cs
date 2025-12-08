// <copyright file="AesGcmConcurrencyTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Services;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

/// <summary>
/// Concurrency tests for AesGcmEncryption to verify thread-safety.
/// Tests the per-call AesGcm instance approach that replaced the shared instance.
/// </summary>
public class AesGcmConcurrencyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly byte[] _testKey;
    private AesGcmEncryption? _encryption;

    public AesGcmConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
        // Generate a random 256-bit key for testing
        _testKey = new byte[32];
        RandomNumberGenerator.Fill(_testKey);
    }

    /// <summary>
    /// Main concurrency test: 100 parallel tasks encrypting and decrypting random data.
    /// This test verifies that the per-call AesGcm instance approach is thread-safe.
    /// </summary>
    [Fact]
    public async Task ParallelEncryptDecrypt_100Tasks_AllSucceed()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int taskCount = 100;
        const int dataSize = 1024; // 1KB per task
        var tasks = new Task[taskCount];
        var results = new ConcurrentBag<(bool success, string error)>();

        _output.WriteLine($"Starting {taskCount} parallel encryption/decryption tasks...");

        // Act - spawn 100 parallel tasks
        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    // Generate random plaintext
                    var plaintext = new byte[dataSize];
                    RandomNumberGenerator.Fill(plaintext);

                    // Encrypt
                    var encrypted = _encryption.Encrypt(plaintext);

                    // Verify encrypted data is different from plaintext (unless by extreme chance)
                    if (encrypted.SequenceEqual(plaintext))
                    {
                        results.Add((false, $"Task {taskId}: Encrypted data matches plaintext (should be different)"));
                        return;
                    }

                    // Decrypt
                    var decrypted = _encryption.Decrypt(encrypted);

                    // Verify plaintext == decrypted
                    if (!plaintext.SequenceEqual(decrypted))
                    {
                        results.Add((false, $"Task {taskId}: Decrypted data does not match original plaintext"));
                        return;
                    }

                    // Verify lengths
                    if (decrypted.Length != plaintext.Length)
                    {
                        results.Add((false, $"Task {taskId}: Length mismatch - plaintext: {plaintext.Length}, decrypted: {decrypted.Length}"));
                        return;
                    }

                    results.Add((true, $"Task {taskId}: Success"));
                }
                catch (Exception ex)
                {
                    results.Add((false, $"Task {taskId}: Exception - {ex.Message}"));
                }
            });
        }

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);

        // Assert
        var failures = results.Where(r => !r.success).ToList();
        
        _output.WriteLine($"Completed {taskCount} tasks. Successes: {results.Count(r => r.success)}, Failures: {failures.Count}");
        
        foreach (var failure in failures)
        {
            _output.WriteLine($"FAILURE: {failure.error}");
        }

        Assert.Empty(failures); // All tasks should succeed
        Assert.Equal(taskCount, results.Count); // All tasks should complete
    }

    /// <summary>
    /// Tests concurrent encryption with varying data sizes to stress ArrayPool usage.
    /// </summary>
    [Fact]
    public async Task ParallelEncryptDecrypt_VariableDataSizes_AllSucceed()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int taskCount = 100;
        var tasks = new Task<bool>[taskCount];
        
        _output.WriteLine("Testing with variable data sizes (16 bytes to 64KB)...");

        // Act - use varying data sizes
        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            // Data size varies from 16 bytes to 64KB
            int dataSize = 16 * (1 << (i % 12)); // 16, 32, 64, 128, ..., 32768, 65536
            
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    var plaintext = new byte[dataSize];
                    RandomNumberGenerator.Fill(plaintext);

                    var encrypted = _encryption.Encrypt(plaintext);
                    var decrypted = _encryption.Decrypt(encrypted);

                    return plaintext.SequenceEqual(decrypted);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Task {taskId} (size {dataSize}): Exception - {ex.Message}");
                    return false;
                }
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r);
        _output.WriteLine($"Variable size test: {successCount}/{taskCount} successful");
        
        Assert.All(results, result => Assert.True(result));
    }

    /// <summary>
    /// Tests repeated encrypt/decrypt cycles in parallel to detect state corruption.
    /// </summary>
    [Fact]
    public async Task ParallelRepeatedCycles_NoStateCorruption()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int workerCount = 50;
        const int cyclesPerWorker = 20;
        var tasks = new Task<int>[workerCount];
        
        _output.WriteLine($"Running {workerCount} workers, {cyclesPerWorker} cycles each...");

        // Act - each worker performs multiple encrypt/decrypt cycles
        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i;
            tasks[i] = Task.Run(() =>
            {
                int successCount = 0;
                for (int cycle = 0; cycle < cyclesPerWorker; cycle++)
                {
                    var plaintext = Encoding.UTF8.GetBytes($"Worker {workerId} - Cycle {cycle} - {Guid.NewGuid()}");
                    
                    var encrypted = _encryption.Encrypt(plaintext);
                    var decrypted = _encryption.Decrypt(encrypted);

                    if (plaintext.SequenceEqual(decrypted))
                    {
                        successCount++;
                    }
                    else
                    {
                        _output.WriteLine($"Worker {workerId}, Cycle {cycle}: Data corruption detected!");
                    }
                }
                return successCount;
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var totalSuccess = results.Sum();
        var expectedSuccess = workerCount * cyclesPerWorker;
        
        _output.WriteLine($"Repeated cycles: {totalSuccess}/{expectedSuccess} successful");
        Assert.Equal(expectedSuccess, totalSuccess);
    }

    /// <summary>
    /// Tests Span-based encrypt/decrypt methods under concurrent load.
    /// </summary>
    [Fact]
    public async Task ParallelSpanEncryptDecrypt_100Tasks_AllSucceed()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int taskCount = 100;
        const int dataSize = 512;
        var tasks = new Task<bool>[taskCount];

        _output.WriteLine("Testing Span-based methods with 100 parallel tasks...");

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    var plaintext = new byte[dataSize];
                    RandomNumberGenerator.Fill(plaintext);

                    // Encrypt using Span overload
                    var nonceSize = AesGcm.NonceByteSizes.MaxSize;
                    var tagSize = AesGcm.TagByteSizes.MaxSize;
                    var encryptedBuffer = new byte[nonceSize + dataSize + tagSize];
                    
                    int encryptedLength = _encryption.Encrypt(plaintext.AsSpan(), encryptedBuffer.AsSpan());

                    // Decrypt using Span overload
                    var decryptedBuffer = new byte[dataSize];
                    int decryptedLength = _encryption.Decrypt(encryptedBuffer.AsSpan(0, encryptedLength), decryptedBuffer.AsSpan());

                    // Verify
                    return plaintext.AsSpan().SequenceEqual(decryptedBuffer.AsSpan(0, decryptedLength));
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Task {taskId}: Exception - {ex.Message}");
                    return false;
                }
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r);
        _output.WriteLine($"Span-based test: {successCount}/{taskCount} successful");
        
        Assert.All(results, result => Assert.True(result));
    }

    /// <summary>
    /// Stress test: High contention with minimal work per task to maximize thread-safety issues.
    /// </summary>
    [Fact]
    public async Task StressTest_HighContention_NoErrors()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int taskCount = 200;
        const int dataSize = 64; // Small data to maximize contention
        var exceptions = new ConcurrentBag<Exception>();
        var successCount = 0;

        _output.WriteLine($"Stress test: {taskCount} tasks with high contention...");

        // Act
        var tasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(() =>
        {
            try
            {
                var plaintext = new byte[dataSize];
                plaintext[0] = (byte)i;
                plaintext[dataSize - 1] = (byte)(i >> 8);

                var encrypted = _encryption.Encrypt(plaintext);
                var decrypted = _encryption.Decrypt(encrypted);

                if (plaintext.SequenceEqual(decrypted))
                {
                    Interlocked.Increment(ref successCount);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        _output.WriteLine($"Stress test: {successCount}/{taskCount} successful, {exceptions.Count} exceptions");
        
        foreach (var ex in exceptions)
        {
            _output.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}");
        }

        Assert.Empty(exceptions);
        Assert.Equal(taskCount, successCount);
    }

    /// <summary>
    /// Tests that different AesGcmEncryption instances can be used concurrently.
    /// </summary>
    [Fact]
    public async Task MultipleInstances_ConcurrentUsage_AllSucceed()
    {
        // Arrange
        const int instanceCount = 10;
        const int tasksPerInstance = 10;
        var allTasks = new List<Task<bool>>();

        _output.WriteLine($"Testing {instanceCount} instances, {tasksPerInstance} tasks each...");

        // Act - create multiple instances and use them concurrently
        for (int inst = 0; inst < instanceCount; inst++)
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var instance = new AesGcmEncryption(key, disableEncrypt: false);

            for (int task = 0; task < tasksPerInstance; task++)
            {
                int instanceId = inst;
                int taskId = task;
                
                allTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var plaintext = Encoding.UTF8.GetBytes($"Instance {instanceId}, Task {taskId}");
                        var encrypted = instance.Encrypt(plaintext);
                        var decrypted = instance.Decrypt(encrypted);
                        return plaintext.SequenceEqual(decrypted);
                    }
                    catch
                    {
                        return false;
                    }
                }));
            }
        }

        var results = await Task.WhenAll(allTasks);

        // Assert
        var successCount = results.Count(r => r);
        _output.WriteLine($"Multiple instances test: {successCount}/{results.Length} successful");
        
        Assert.All(results, result => Assert.True(result));
    }

    /// <summary>
    /// Tests encryption/decryption with the same data from multiple threads to detect nonce reuse.
    /// Each encryption should produce different ciphertext due to random nonce.
    /// </summary>
    [Fact]
    public async Task ParallelEncryptSameData_UniqueNonces_DifferentCiphertexts()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int taskCount = 100;
        var plaintext = Encoding.UTF8.GetBytes("This is the same plaintext for all tasks");
        var encryptedResults = new ConcurrentBag<byte[]>();

        _output.WriteLine("Encrypting same plaintext 100 times in parallel...");

        // Act - encrypt the same plaintext multiple times
        var tasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(() =>
        {
            var encrypted = _encryption.Encrypt(plaintext);
            encryptedResults.Add(encrypted);
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var encryptedList = encryptedResults.ToList();
        Assert.Equal(taskCount, encryptedList.Count);

        // Verify all ciphertexts are unique (due to different random nonces)
        var uniqueCiphertexts = encryptedList.Select(e => Convert.ToBase64String(e)).Distinct().Count();
        
        _output.WriteLine($"Unique ciphertexts: {uniqueCiphertexts}/{taskCount}");
        Assert.Equal(taskCount, uniqueCiphertexts); // All should be unique

        // Verify all decrypt back to the same plaintext
        foreach (var encrypted in encryptedList)
        {
            var decrypted = _encryption.Decrypt(encrypted);
            Assert.True(plaintext.SequenceEqual(decrypted));
        }
    }

    /// <summary>
    /// Tests that ArrayPool buffers are properly returned and don't cause issues under load.
    /// </summary>
    [Fact]
    public async Task ArrayPoolStressTest_NoLeaksOrCorruption()
    {
        // Arrange
        _encryption = new AesGcmEncryption(_testKey, disableEncrypt: false);
        const int iterationCount = 1000;
        const int parallelism = 20;
        var errorCount = 0;

        _output.WriteLine($"ArrayPool stress test: {iterationCount} iterations, {parallelism} parallel...");

        // Act - rapid fire encrypt/decrypt to stress ArrayPool
        var batches = Enumerable.Range(0, iterationCount / parallelism);
        
        foreach (var batch in batches)
        {
            var tasks = Enumerable.Range(0, parallelism).Select(i => Task.Run(() =>
            {
                try
                {
                    var data = new byte[256];
                    RandomNumberGenerator.Fill(data);
                    
                    var encrypted = _encryption.Encrypt(data);
                    var decrypted = _encryption.Decrypt(encrypted);
                    
                    if (!data.SequenceEqual(decrypted))
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
        }

        // Assert
        _output.WriteLine($"ArrayPool stress test: {errorCount} errors out of {iterationCount} operations");
        Assert.Equal(0, errorCount);
    }

    public void Dispose()
    {
        _encryption?.Dispose();
    }
}
