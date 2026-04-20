# Are Nullable Types Semantically Equivalent to Optional Types?

**Short answer: No — and here's a single real example that kills the argument.**

---

## The Claim

> "Types which can be nullable are semantically (and compile-time) equivalent to optional types. We already have `null` as 'absence is explicit.' Unless you want different values of absence, isn't `string?` the same as `Option<string>`?"

## The Killer Counter-Example

```csharp
Dictionary<string, object> row = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2")[0];

string name = (string)row["Name"];   // Compiler: ✅ fine, object is non-null
                                      // Runtime:  💥 NullReferenceException
```

The **type is non-nullable**. The **value is null**. The compiler is *happy*. The app crashes.

This isn't contrived — it's every ORM, every `DataReader`, every JSON deserializer, every dictionary lookup in every real application. The moment data crosses a boundary (database, network, file, reflection, interop), the compiler's nullability tracking is **erased**.

`Option<T>` doesn't have this failure mode. You physically cannot access the inner value without pattern-matching on `Some`/`None`. The absence is encoded in the *value*, not in a *compiler annotation that the runtime ignores*.

## The Precise Distinction

| Property | `string?` (NRT) | `Option<string>` |
|---|---|---|
| Where enforced | Compile-time annotation only | Runtime value — the type *is* the check |
| Reflection/deserialization bypass | Yes — trivially | No — you get `None`, not a secret null |
| Composable | No — manual `if (x != null)` chains | Yes — `.Map()`, `.Bind()`, `.Match()` |
| Proves absence was handled | No — warnings are suppressible, not errors | Yes — won't compile without handling both arms |
| Works across trust boundaries | No — external data ignores your annotations | Yes — the boundary returns `Option<T>` |

## Why "Different Values of Absence" Is a Red Herring

The argument isn't about *more kinds of absence*. It's about **where** the absence is enforced:

- **NRT**: The compiler *believes* the annotation. The runtime doesn't. That's a **lie** the type system tells itself.
- **Option\<T\>**: The value *is* the proof. There is no gap between what the compiler knows and what the runtime does.

The statement *"runtime data semantics cannot always be fully proven at compile time"* means exactly this: **the compiler can annotate intent, but it cannot enforce contracts on data it has never seen** (SQL results, JSON payloads, reflection-populated DTOs). `Option<T>` closes that gap by making the proof travel *with the value*.

## TL;DR

> **"Aren't nullable types just optional types?"**
>
> No. `string?` is a *compile-time promise* the runtime is free to break — and does, every time data comes from a database, API, or deserializer. `Option<T>` is a *runtime-enforced value* that won't let you touch the data without proving you handled absence. The gap between annotation and enforcement is where real apps crash.
>
> Full write-up → [Functional Null Safety in SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/FUNCTIONAL_NULL_SAFETY.md)

---

*See also: [FUNCTIONAL_NULL_SAFETY.md](./FUNCTIONAL_NULL_SAFETY.md) for tested examples, benchmarks, and the full `Option<T>` / `Fin<T>` API in SharpCoreDB v1.7.0.*
