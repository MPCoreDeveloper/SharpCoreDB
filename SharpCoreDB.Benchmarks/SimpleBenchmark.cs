// <copyright file="SimpleBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Simple benchmark to verify BenchmarkDotNet is working correctly.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class SimpleBenchmark
{
    private List<int> numbers = null!;

    [Params(10, 100)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        numbers = Enumerable.Range(1, N).ToList();
        Console.WriteLine($"? SimpleBenchmark Setup completed with N={N}");
    }

    [Benchmark(Baseline = true)]
    public int Sum_ForLoop()
    {
        int sum = 0;
        for (int i = 0; i < numbers.Count; i++)
        {
            sum += numbers[i];
        }
        return sum;
    }

    [Benchmark]
    public int Sum_Foreach()
    {
        int sum = 0;
        foreach (var num in numbers)
        {
            sum += num;
        }
        return sum;
    }

    [Benchmark]
    public int Sum_Linq()
    {
        return numbers.Sum();
    }
}
