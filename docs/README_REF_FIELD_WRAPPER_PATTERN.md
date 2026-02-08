# 🛡️ Ref-Field Wrapper Pattern: Preventing Silent Struct Copy Bugs

## Het Probleem

Bij het itereren over mutable structs in C# is het **gevaarlijk makkelijk** om het `ref` keyword te vergeten:

```csharp
// ❌ BUG: 'handle' is een KOPIE — mutaties gaan stilletjes verloren!
foreach (var handle in store.EnumerateHandles())
{
    handle.Clear();  // Muteert een tijdelijke kopie, origineel blijft ongewijzigd
}

// ✅ CORRECT: 'ref' zorgt dat we het origineel muteren
foreach (ref var handle in store.EnumerateHandles())
{
    handle.Clear();  // Muteert het origineel in-place
}
```

Er is **geen compiler warning** voor deze fout. De code compileert en draait — maar doet stilletjes niets. Dit leidt tot extreem moeilijk te vinden bugs.

> **Oorspronkelijke vraag:** *"I wish there was a way to prevent copies like in C++, or at least trigger a warning."*

---

## De Oplossing: Ref-Field Wrapper Struct

C# 11 introduceerde **ref fields** — een veld in een `ref struct` dat een *referentie* naar een andere waarde vasthoudt. We gebruiken dit om een dunne wrapper te maken die **altijd naar het origineel wijst**, zelfs als de wrapper zelf gekopieerd wordt.

### Hoe Het Werkt

```
┌─────────────────────────────────────────────────────┐
│  foreach (var wrapper in ...)                       │
│                                                     │
│  wrapper (stack kopie)                              │
│  ┌──────────────────────┐                           │
│  │ ref Handle Value ──────────► Origineel Handle[i] │
│  └──────────────────────┘       in Span<Handle>     │
│                                                     │
│  Kopiëren van de wrapper kopieert de REF,           │
│  niet de data.                                      │
│  wrapper.Clear() muteert altijd het origineel.      │
└─────────────────────────────────────────────────────┘
```

| Scenario | Zonder Wrapper | Met Wrapper |
|---|---|---|
| `foreach (ref var h in ...)` | ✅ Muteert origineel | ✅ Muteert origineel |
| `foreach (var h in ...)` | ❌ **Stille bug** — muteert kopie | ✅ **Muteert nog steeds origineel** |

---

## Het Patroon (3 Stappen)

### Stap 1: Definieer de Wrapper

```csharp
public readonly ref struct RefHandle
{
    public readonly ref Handle Value;  // C# 11+ ref field

    public RefHandle(ref Handle value) => Value = ref value;

    // Forward mutatie-methoden voor ergonomische API
    public void Clear() => Value.Clear();
    public int Id => Value.Id;
    public bool IsActive => Value.IsActive;
}
```

### Stap 2: Enumerator Levert Wrappers

```csharp
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
    public HandleEnumerator GetEnumerator() => this;
}
```

### Stap 3: Gebruik Het — `ref` Keyword Niet Meer Nodig

```csharp
// Beide muteren correct de originelen:
foreach (var handle in store.EnumerateHandles())   // ✅ Veilig!
    handle.Clear();

foreach (ref var handle in store.EnumerateHandles()) // ✅ Ook veilig, maar overbodig
    handle.Value.Clear();
```

---

## Eigenschappen

| Eigenschap | Waarde |
|---|---|
| **Runtime kosten** | Nul — stack-only, geen heap allocaties |
| **Unsafe code** | Niet nodig |
| **Custom analyzers** | Niet nodig |
| **Minimum C# versie** | C# 11 (`ref` fields) |
| **Werkt met** | `Span<T>`, `ref struct` enumerators |

---

## Demo Draaien

```bash
cd tests/Manual/RefFieldDemo
dotnet run
```

### Verwachte Output

```
=== Ref-Field Wrapper Pattern Demo ===

── Demo 1: 'foreach (var h in ...)' WITH wrapper ──
   (No 'ref' keyword — should still mutate originals)

   Before:
     Handle(Id=1, IsActive=True)
     Handle(Id=2, IsActive=True)
     Handle(Id=3, IsActive=True)
   After Clear() via 'var':
     Handle(Id=0, IsActive=False)
     Handle(Id=0, IsActive=False)
     Handle(Id=0, IsActive=False)

   ✅ SUCCESS: All handles mutated in-place WITHOUT 'ref'!

── Demo 2: Explicit wrapper copy still mutates original ──

   Before:  Handle(Id=42, IsActive=True)
   After copy2.Clear():  Handle(Id=0, IsActive=False)
   ✅ SUCCESS: Double-copied wrapper still mutated the original!

── Demo 3: The BUG without wrapper ──
   (Raw Span<T> + foreach var = silent copy bug)

   Before:
     Handle(Id=10, IsActive=True)
     Handle(Id=20, IsActive=True)
   After Clear() via 'var' (NO wrapper):
     Handle(Id=10, IsActive=True)
     Handle(Id=20, IsActive=True)

   ⚠️  BUG DEMONSTRATED: Originals are UNCHANGED — Clear() was lost!
   → This is exactly the bug the wrapper pattern prevents.
```

---

## Wanneer Dit Patroon Gebruiken

- Je slaat **mutable structs** op in aaneengesloten geheugen (`Span<T>`, arrays)
- Je enumereert ze en moet **in-place muteren**
- Je wilt de **hele klasse van bugs elimineren** waar `ref` vergeten wordt
- Je hebt **zero-allocation** iteratie nodig

---

## Alternatief: Delegate-Based Approach

Als je geen wrapper wilt, kun je een callback-patroon gebruiken:

```csharp
public delegate void RefAction<T>(ref T item);

public void ForEachHandle(RefAction<Handle> action)
{
    for (int i = 0; i < _handles.Length; i++)
    {
        action(ref _handles[i]);
    }
}

// Gebruik:
store.ForEachHandle(static (ref Handle h) => h.Clear());
```

Dit is minder ergonomisch maar ook veilig — de caller kan `ref` niet vergeten omdat de delegate het afdwingt.

---

## Zie Ook

- [C# 11 ref fields specificatie](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct#ref-fields)
- [`tests/Manual/RefFieldDemo/`](../tests/Manual/RefFieldDemo/) — Werkende demo
