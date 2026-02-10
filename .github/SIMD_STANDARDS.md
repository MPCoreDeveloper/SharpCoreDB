# SIMD API Standards — SharpCoreDB

> **Mandatory for all SIMD code in SharpCoreDB.** Non-compliant code will be rejected in review.

## Required API: `System.Runtime.Intrinsics`

All SIMD code **MUST** use the explicit intrinsics from `System.Runtime.Intrinsics`:

```csharp
// ✅ REQUIRED — explicit multi-tier intrinsics
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

Vector512<float> v512 = Vector512.LoadUnsafe(ref data);
Vector256<float> v256 = Vector256.LoadUnsafe(ref data);
Vector128<float> v128 = Vector128.LoadUnsafe(ref data);
```

## Banned API: `System.Numerics.Vector<T>`

**DO NOT** use the old portable `Vector<T>` from `System.Numerics`:

```csharp
// ❌ BANNED — old portable SIMD (no explicit ISA control)
using System.Numerics;

Vector<float>.Count;              // ❌
Vector.IsHardwareAccelerated;     // ❌
MemoryMarshal.Cast<float, Vector<float>>(...); // ❌
Vector.Sum(...);                  // ❌
```

### Why?

As recommended by Tanner Gooding (.NET Runtime team), `System.Runtime.Intrinsics` is the
modern, preferred API for .NET 8+ / .NET 10:

| Feature | `System.Numerics.Vector<T>` (OLD) | `System.Runtime.Intrinsics` (NEW) |
|---|---|---|
| ISA control | JIT decides width | Explicit per-tier |
| AVX-512 | No explicit support | Full `Avx512F` access |
| FMA | Not accessible | `Fma.MultiplyAdd()` |
| NativeAOT | Width may vary | Deterministic codegen |
| Instruction selection | Opaque | You choose the instruction |
| Multi-tier dispatch | Not possible | AVX-512 → AVX2 → SSE → Scalar |

## Required Multi-Tier Dispatch Pattern

Every SIMD hot path must implement a tiered fallback chain:

```csharp
if (Avx512F.IsSupported && len >= AVX512_THRESHOLD)
{
    // Vector512<T> path
}
else if (Avx2.IsSupported && len >= 8)  // or Sse.IsSupported for float
{
    // Vector256<T> path
}
else if (Sse.IsSupported && len >= 4)   // or Sse2 for int/double
{
    // Vector128<T> path
}

// Scalar tail — ALWAYS required
for (; i < len; i++) { /* scalar */ }
```

### ISA Check Mapping

| Data Type | 512-bit | 256-bit | 128-bit |
|---|---|---|---|
| `float` | `Avx512F.IsSupported` | `Avx2.IsSupported` | `Sse.IsSupported` |
| `double` | `Avx512F.IsSupported` | `Avx2.IsSupported` | `Sse2.IsSupported` |
| `int` | `Avx512F.IsSupported` | `Avx2.IsSupported` | `Sse2.IsSupported` |
| `long` | `Avx512F.IsSupported` | `Avx2.IsSupported` | `Sse2.IsSupported` |
| `byte` (XOR/popcount) | `Avx512BW.IsSupported` | `Avx2.IsSupported` | `Sse2.IsSupported` |

## Required Patterns

### Loading Data (use `LoadUnsafe`, not pointer-based loads)

```csharp
// ✅ DO — safe ref-based loading (no 'fixed', no unsafe)
ref float refData = ref MemoryMarshal.GetReference(span);
var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref refData, i));

// ✅ ALSO OK — pointer-based when already in unsafe context
var vec = Avx.LoadVector256(ptr + i);

// ❌ DON'T — old MemoryMarshal.Cast to Vector<T>
var vecs = MemoryMarshal.Cast<float, Vector<float>>(span);
```

### FMA (Fused Multiply-Add)

Always use FMA when available — better throughput AND precision:

```csharp
// ✅ DO — FMA with fallback
if (Fma.IsSupported)
    vSum = Fma.MultiplyAdd(va, vb, vSum);   // a*b + c in one instruction
else
    vSum += va * vb;

// ✅ DO — AVX-512 always has FMA
vSum = Avx512F.FusedMultiplyAdd(va, vb, vSum);
```

### Horizontal Reduction

```csharp
// ✅ DO — cross-platform Vector*.Sum()
float result = Vector256.Sum(vAccumulator);

// ❌ DON'T — old System.Numerics
float result = Vector.Sum(vAccumulator);
```

### Storing Results

```csharp
// ✅ DO
result.StoreUnsafe(ref Unsafe.Add(ref refDst, i));

// ❌ DON'T — old MemoryMarshal.Cast to write
var spanDst = MemoryMarshal.Cast<float, Vector<float>>(result.AsSpan());
spanDst[j] = value;
```

## AVX-512 Thresholds

AVX-512 has a frequency throttling cost on some CPUs. Use minimum element thresholds:

| Use Case | Minimum Elements |
|---|---|
| WHERE filtering (int/double) | 1024 |
| Distance metrics (float) | 64 |
| Batch XOR / Hamming | 32 bytes |

## Reference Implementations

- **WHERE filtering**: `src/SharpCoreDB/Optimizations/SimdWhereFilter.cs`
- **Distance metrics**: `src/SharpCoreDB.VectorSearch/Distance/DistanceMetrics.cs`
- **Hamming distance**: `src/SharpCoreDB.VectorSearch/Quantization/BinaryQuantizer.cs`

## Code Review Checklist

- [ ] No `System.Numerics.Vector<T>` usage
- [ ] No `Vector.IsHardwareAccelerated` checks
- [ ] Uses `Vector128<T>` / `Vector256<T>` / `Vector512<T>` from `System.Runtime.Intrinsics`
- [ ] Multi-tier dispatch: AVX-512 → AVX2 → SSE → Scalar
- [ ] Scalar tail for remainder elements
- [ ] FMA used where available (`Fma.MultiplyAdd` / `Avx512F.FusedMultiplyAdd`)
- [ ] `[MethodImpl(MethodImplOptions.AggressiveOptimization)]` on hot paths
- [ ] AVX-512 guarded by minimum element threshold

---

**Enforcement:** All new and modified SIMD code must comply. Existing violations should be migrated on contact.  
**Last Updated:** 2025-07-08
