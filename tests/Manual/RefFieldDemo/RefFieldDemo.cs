// <copyright file="RefFieldDemo.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>


using System.Runtime.InteropServices;

/// IDEA
/// using System;

//public class Ref
//{
//    // Wrapper-based 
//    //1. Define the Wrapper
//    public readonly struct RefHandle   // can be readonly struct for extra safety
//    {
//        public readonly ref Handle Value;   // C# 11+ ref field

//        public RefHandle(ref Handle value) => Value = ref value;

//        // Forward important mutating methods for clean usage
//        public void Clear() => Value.Clear();

//        // Forward other members as needed, e.g.:
//        // public int Id => Value.Id;
//        // public void MarkAsUsed() => Value.MarkAsUsed();
//    }
//    //2. Update the Enumerator to Yield the Wrapper
//    public ref struct HandleEnumerator   // often a ref struct for zero-allocation
//    {
//        private readonly Span<Handle> _span;   // or your storage view
//        private int _index = -1;

//        public HandleEnumerator(Span<Handle> span) => _span = span;

//        public bool MoveNext() => ++_index < _span.Length;

//        public RefHandle Current => new RefHandle(ref _span[_index]);
//    }
//    //3. Expose It from Your Collection/Store
//    public HandleEnumerator EnumerateHandlesOfType(HandleType type)
//    {
//        // Return enumerator over the relevant slice/span
//        return new HandleEnumerator(GetSpanForType(type));
//    }


//    public void ExampleUsage()
//    {

//        foreach (var refHandle in _gcHandleManager.Store.EnumerateHandlesOfType(HandleType.HNDTYPE_STRONG))
//        {
//            refHandle.Clear();          // Always mutates original — no 'ref' needed!
//                                        // refHandle.Value.Clear(); // Alternative if you don't forward methods
//        }
//        //Even this is harmless (still mutates original):
//        foreach (var copyOfWrapper in …)
//{
//            copyOfWrapper.Clear();      // ref field still points to original location
//        }

//        /// GOOD
//        //The ref is encapsulated inside the wrapper struct
//        //Copying the wrapper struct copies the reference, not the underlying Handle
//        //No runtime cost(stack-only, tiny struct)
//        //No unsafe, no pointers, no custom analyzers
//        //API is discoverable and ergonomic(especially with method forwarding)
//        //Works great with ref struct enumerators / spans

//    }
//    // Delegate-based alternative(if you prefer no wrapper)
//    public void ForEachHandlesOfType(HandleType type, ActionRef<Handle> action)
//        where ActionRef<Handle> : delegate*<ref Handle, void>
//{
//    var e = EnumerateHandlesOfTypeInternal(type);
//    while (e.MoveNext())
//        action(ref e.Current);
//}
//public void ExampleUsage()
//{
//    gcHandleManager.Store.ForEachHandlesOfType(HandleType.HNDTYPE_STRONG,
//    static ref h => h.Clear());
//}
   

//}






/// <summary>
/// Demonstrates the ref-field wrapper pattern that prevents silent struct copy bugs.
///
/// Problem:  forgetting <c>ref</c> in <c>foreach (ref var h in ...)</c> silently mutates a copy.
/// Solution: wrap the ref in a readonly ref struct — copies of the wrapper still point to the original.
/// </summary>
internal static class RefFieldDemo
{
    // ── The mutable struct we want to modify in-place ─────────────

    /// <summary>
    /// A mutable value type stored in contiguous memory.
    /// Must be mutated in-place (no accidental copies allowed).
    /// </summary>
    public struct Handle
    {
        public int Id;
        public bool IsActive;

        public void Clear()
        {
            Id = 0;
            IsActive = false;
        }

        public override readonly string ToString() =>
            $"Handle(Id={Id}, IsActive={IsActive})";
    }

    // ── Step 1: The Wrapper ───────────────────────────────────────

    /// <summary>
    /// Wraps a <c>ref Handle</c> so that even a value-copy of this
    /// wrapper still points to the original <see cref="Handle"/>.
    /// C# 11+ ref field ensures the reference is never lost.
    /// </summary>
    public readonly ref struct RefHandle
    {
        public readonly ref Handle Value;

        public RefHandle(ref Handle value) => Value = ref value;

        // Forwarded mutation methods — callers never need to touch .Value
        public void Clear() => Value.Clear();
        public int Id => Value.Id;
        public bool IsActive => Value.IsActive;
    }

    // ── Step 2: The Enumerator ────────────────────────────────────

    /// <summary>
    /// Zero-allocation enumerator that yields <see cref="RefHandle"/> wrappers.
    /// Because <see cref="RefHandle"/> contains a ref field, forgetting <c>ref</c>
    /// in a foreach loop is no longer a bug.
    /// </summary>
    public ref struct HandleEnumerator
    {
        private readonly Span<Handle> _span;
        private int _index;

        public HandleEnumerator(Span<Handle> span)
        {
            _span = span;
            _index = -1;
        }

        public bool MoveNext() => ++_index < _span.Length;

        public RefHandle Current => new(ref _span[_index]);

        // Required for the foreach pattern (duck-typing)
        public readonly HandleEnumerator GetEnumerator() => this;
    }

    // ── Step 3: The Store / Collection ────────────────────────────

    /// <summary>
    /// Simple store that holds handles and exposes the safe enumerator.
    /// </summary>
    public readonly ref struct HandleStore
    {
        private readonly Span<Handle> _handles;

        public HandleStore(Span<Handle> handles) => _handles = handles;

        /// <summary>
        /// Returns a zero-allocation enumerator that is safe to use
        /// with <c>foreach (var h in ...)</c> — no <c>ref</c> required.
        /// </summary>
        public HandleEnumerator EnumerateHandles() => new(_handles);

        public ReadOnlySpan<Handle> AsReadOnly() => _handles;
    }

    // ── Demo Entry Point ──────────────────────────────────────────

    public static void Main()
    {
        Console.WriteLine("=== Ref-Field Wrapper Pattern Demo ===");
        Console.WriteLine();

        // ── Demo 1: Prove that 'var' (without ref) still mutates originals ──

        Demo1_VarWithoutRef();

        Console.WriteLine();

        // ── Demo 2: Prove wrapper copy also mutates originals ──

        Demo2_WrapperCopyStillMutates();

        Console.WriteLine();

        // ── Demo 3: Show the BUG without wrapper (raw ref struct enum) ──

        Demo3_ShowBugWithoutWrapper();
    }

    /// <summary>
    /// Demonstrates that <c>foreach (var h in ...)</c> with the wrapper
    /// correctly mutates the original handles — no <c>ref</c> needed.
    /// </summary>
    private static void Demo1_VarWithoutRef()
    {
        Console.WriteLine("── Demo 1: 'foreach (var h in ...)' WITH wrapper ──");
        Console.WriteLine("   (No 'ref' keyword — should still mutate originals)");
        Console.WriteLine();

        // Arrange
        Span<Handle> handles =
        [
            new Handle { Id = 1, IsActive = true },
            new Handle { Id = 2, IsActive = true },
            new Handle { Id = 3, IsActive = true },
        ];

        var store = new HandleStore(handles);

        Console.WriteLine("   Before:");
        foreach (var h in store.EnumerateHandles())
        {
            Console.WriteLine($"     {h.Value}");
        }

        // Act — using 'var', NOT 'ref var'
        foreach (var handle in store.EnumerateHandles())
        {
            handle.Clear();
        }

        // Assert
        Console.WriteLine("   After Clear() via 'var':");
        bool allCleared = true;
        foreach (var h in store.EnumerateHandles())
        {
            Console.WriteLine($"     {h.Value}");
            if (h.Id != 0 || h.IsActive)
                allCleared = false;
        }

        Console.WriteLine();
        Console.WriteLine(allCleared
            ? "   ✅ SUCCESS: All handles mutated in-place WITHOUT 'ref'!"
            : "   ❌ FAIL: Some handles were not mutated.");
    }

    /// <summary>
    /// Demonstrates that even explicitly copying the wrapper to a local variable
    /// still mutates the original — the ref field follows.
    /// </summary>
    private static void Demo2_WrapperCopyStillMutates()
    {
        Console.WriteLine("── Demo 2: Explicit wrapper copy still mutates original ──");
        Console.WriteLine();

        // Arrange
        Span<Handle> handles =
        [
            new Handle { Id = 42, IsActive = true },
        ];

        Console.WriteLine($"   Before:  {handles[0]}");

        // Act — deliberately copy the wrapper
        var enumerator = new HandleEnumerator(handles);
        enumerator.MoveNext();

        var copy1 = enumerator.Current;  // copy of wrapper
        var copy2 = copy1;               // copy of the copy!

        copy2.Clear();  // should STILL mutate handles[0]

        Console.WriteLine($"   After copy2.Clear():  {handles[0]}");
        Console.WriteLine(handles[0].Id == 0 && !handles[0].IsActive
            ? "   ✅ SUCCESS: Double-copied wrapper still mutated the original!"
            : "   ❌ FAIL: Copy did not reach original.");
    }

    /// <summary>
    /// Shows the classic bug: without the wrapper, forgetting <c>ref</c>
    /// in a foreach silently mutates a temporary copy.
    /// </summary>
    private static void Demo3_ShowBugWithoutWrapper()
    {
        Console.WriteLine("── Demo 3: The BUG without wrapper ──");
        Console.WriteLine("   (Raw Span<T> + foreach var = silent copy bug)");
        Console.WriteLine();

        // Arrange
        Handle[] handles =
        [
            new Handle { Id = 10, IsActive = true },
            new Handle { Id = 20, IsActive = true },
        ];

        Console.WriteLine("   Before:");
        foreach (var h in handles)
        {
            Console.WriteLine($"     {h}");
        }

        // Act — BUG: 'var' without 'ref' copies the struct
        foreach (var handle in handles.AsSpan())
        {
            handle.Clear();  // ⚠️ Mutates a COPY, not the original!
        }

        Console.WriteLine("   After Clear() via 'var' (NO wrapper):");
        bool anyCleared = false;
        foreach (var h in handles)
        {
            Console.WriteLine($"     {h}");
            if (h.Id == 0)
                anyCleared = true;
        }

        Console.WriteLine();
        Console.WriteLine(anyCleared
            ? "   (Unexpected: something was cleared)"
            : "   ⚠️  BUG DEMONSTRATED: Originals are UNCHANGED — Clear() was lost!");
        Console.WriteLine("   → This is exactly the bug the wrapper pattern prevents.");
    }
}
