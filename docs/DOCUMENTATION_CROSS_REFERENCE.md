# Documentation Cross-Reference Guide

## 📚 SharpCoreDB Documentation Ecosystem

### Two Documentation Tracks

SharpCoreDB has **two complementary documentation systems**:

#### 🏗️ Design Track: `docs/scdb/FILE_FORMAT_DESIGN.md`
**Purpose:** Architectural design & specifications  
**Audience:** Architects, designers, future developers  
**Content:**
- Overall design principles
- Format specifications (struct definitions)
- Comparison with SQLite, LiteDB
- Performance optimization strategies
- Future extension points

**Key sections:**
- Executive Summary
- File Structure Overview
- Detailed Format Specification (Header, Registry, FSM, WAL, Table Directory)
- Performance Optimizations

#### 🔧 Implementation Track: `docs/serialization/`
**Purpose:** Practical guides for implementing/using serialization  
**Audience:** Developers using SharpCoreDB, implementers  
**Content:**
- Real examples with actual hex dumps
- Step-by-step serialization walkthroughs
- Variable-length string handling (with evidence)
- Free space management (practical examples)
- Block registry lookups (O(1) explanation)
- FAQ with solutions to real problems

**Structure:**
```
docs/serialization/
├── README.md (navigation hub)
├── SERIALIZATION_AND_STORAGE_GUIDE.md (3,200 lines)
├── SERIALIZATION_FAQ.md (800 lines)
├── BINARY_FORMAT_VISUAL_REFERENCE.md (900 lines)
└── scripts/visualize_serialization.py (interactive tool)
```

---

## 🗺️ Content Mapping

### Topic: File Header Structure

**In FILE_FORMAT_DESIGN.md:**
```markdown
### 1. File Header (512 bytes, fixed)

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScdbFileHeader
{
    public readonly ulong Magic;           // 0x0000: "SCDB" + version
    public readonly ushort FormatVersion;  // 0x0008: Format version (1)
    public readonly ushort PageSize;       // 0x000A: Page size in bytes
    // ... (C# struct definitions)
}
```

**Purpose:** Formal specification  
**Detail Level:** Struct definitions, sizes, purpose of each field

**In BINARY_FORMAT_VISUAL_REFERENCE.md:**
```markdown
## 2. File Header Structure (512 bytes)

SCDB File Header Layout:

Offset   Size   Field Name                Value
0x0000   8      Magic                     0x4243445310000000
0x0008   2      FormatVersion             1
0x000A   2      PageSize                  4096 (default)
```

**Purpose:** Visual reference  
**Detail Level:** Hex offsets, byte sizes, visual tables

---

### Topic: Block Registry

**In FILE_FORMAT_DESIGN.md:**
```markdown
### 2. Block Registry (Variable, page-aligned)

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BlockEntry
{
    public readonly fixed byte Name[32];
    public readonly uint BlockType;
    public readonly ulong Offset;
    public readonly ulong Length;
    public readonly fixed byte Checksum[32];
}

// Block Naming Convention:
// Format: "namespace:identifier[:subtype]"
// Examples:
// - table:app_users:data
// - table:app_users:index:pk_users
```

**Purpose:** Specification & architecture  
**Detail Level:** Struct layout, naming conventions, design decisions

**In SERIALIZATION_AND_STORAGE_GUIDE.md:**
```markdown
## Block Registry

### Purpose

The **Block Registry** maps logical block names to physical file locations:

[code example + lookup flow + performance analysis]

### O(1) Lookups

ConcurrentDictionary = O(1) average lookup
Block names stay in hash table
```

**Purpose:** Implementation guide & explanation  
**Detail Level:** How it works in practice, performance implications

---

### Topic: Free Space Management

**In FILE_FORMAT_DESIGN.md:**
```markdown
### 3. Free Space Map (FSM)

Design: Inspired by PostgreSQL's FSM, uses a **two-level bitmap**:
1. **L1 Bitmap:** 1 bit per page (allocated/free)
2. **L2 Extent Map:** Tracks contiguous free extents

[C# struct definitions]

Allocation Strategy:
1. Small allocations (<64 pages): Scan L1 bitmap
2. Large allocations (≥64 pages): Use L2 extent map
3. Defragmentation: Background VACUUM
```

**Purpose:** Architectural design  
**Detail Level:** Design rationale, algorithm overview

**In SERIALIZATION_AND_STORAGE_GUIDE.md:**
```markdown
## Free Space Management

### How FSM Works

The **Free Space Map (FSM)** behaves vrije pagina's. Dit is een 2-level bitmap:

[Detailed explanation with code examples]

### File Growth Strategy

Exponential growth (10MB → 20MB → 40MB...)
Phase 3 optimized: MIN_EXTENSION_PAGES = 2560

[Real-world numbers and examples]
```

**Purpose:** Practical guide  
**Detail Level:** How to use it, examples, optimization tips

---

## ✅ When To Use Which

### Use FILE_FORMAT_DESIGN.md When...

- ❓ *"What is the binary structure of the header?"*
- ❓ *"What are the struct layouts?"*
- ❓ *"How does FSM compare to PostgreSQL?"*
- ❓ *"What are the design principles?"*
- ❓ *"What's the future extension strategy?"*

**Answer:** Go to `docs/scdb/FILE_FORMAT_DESIGN.md`

---

### Use docs/serialization/ When...

- ❓ *"How do I serialize a record?"*
- ❓ *"What are the actual hex bytes for a string?"*
- ❓ *"Do variable-length strings cause fragmentation?"*
- ❓ *"How do I find a record?"*
- ❓ *"What's the performance impact of strings?"*
- ❓ *"How do column boundaries work?"*

**Answer:** Go to `docs/serialization/README.md` → pick the right doc

---

## 📊 Complementary Coverage

### FILE_FORMAT_DESIGN.md Covers:
✅ Overall architecture  
✅ Struct definitions & layouts  
✅ Design decisions & rationale  
✅ Comparison with competitors  
✅ Future extension points  
✅ Performance optimization strategies  
❌ Actual hex dump examples  
❌ Variable-length string handling  
❌ Real-world fragmentation examples  
❌ O(1) lookup explanations  
❌ FAQ with solutions  

### docs/serialization/ Covers:
✅ Real hex dump examples  
✅ Variable-length strings (with evidence!)  
✅ Actual fragmentation examples  
✅ O(1) lookup walkthroughs  
✅ FAQ with solutions  
✅ Visual diagrams  
✅ Interactive Python tool  
✅ Performance comparisons  
❌ Overall architecture (that's in FILE_FORMAT_DESIGN)  
❌ Struct definitions (that's in FILE_FORMAT_DESIGN)  
❌ Design rationale (that's in FILE_FORMAT_DESIGN)  

---

## 🔗 Cross-References

### From FILE_FORMAT_DESIGN.md to serialization docs

**Location:** Add to relevant sections

```markdown
> **For practical examples and real-world usage:**
> See `docs/serialization/SERIALIZATION_AND_STORAGE_GUIDE.md`
```

### From serialization docs to FILE_FORMAT_DESIGN.md

**Location:** Add to relevant sections

```markdown
> **For architectural design and struct definitions:**
> See `docs/scdb/FILE_FORMAT_DESIGN.md`
```

---

## 📋 Example: The Question "Do I need lots of free space?"

### Path 1: Designer's perspective
1. **Question:** "Is FSM efficient? Any design flaws?"
2. **Go to:** `docs/scdb/FILE_FORMAT_DESIGN.md` § "Free Space Map (FSM)"
3. **Learn:** Two-level bitmap design, allocation strategy
4. **Compare:** SQLite, LiteDB approaches

### Path 2: Developer's perspective
1. **Question:** "Do variable-length strings waste space?"
2. **Go to:** `docs/serialization/README.md`
3. **Click:** "Do I need free space?" link
4. **Learn:** Real examples showing 96.9% space savings!
5. **Verify:** With visualize_serialization.py tool

---

## 🎯 Summary

| Dimension | FILE_FORMAT_DESIGN | serialization/ |
|-----------|-------------------|----------------|
| **Audience** | Architects | Developers |
| **Purpose** | Design spec | Implementation guide |
| **Detail** | Struct layout | Real examples |
| **Format** | Formal | Practical |
| **Code** | Struct definitions | Serialization code |
| **Examples** | Design comparisons | Hex dumps, real data |
| **Use case** | Understanding design | Solving problems |

---

## 🚀 Recommendation: Cross-Link Both

Since both are now complete and complementary, consider:

1. ✅ **Keep both separate** - They serve different purposes
2. ✅ **Add cross-references** between them:
   - FILE_FORMAT_DESIGN.md → "See serialization/ for practical examples"
   - serialization/ → "See FILE_FORMAT_DESIGN.md for architecture"
3. ✅ **Update main README.md** to mention both:
   - "Design documentation: `docs/scdb/`"
   - "Implementation guides: `docs/serialization/`"
4. ✅ **Add to root docs/README.md**:
   ```markdown
   ## Documentation Structure
   
   - **Design & Specifications:** `docs/scdb/FILE_FORMAT_DESIGN.md`
     - Architectural overview, struct definitions, design principles
   
   - **Implementation Guides:** `docs/serialization/`
     - Practical tutorials, real examples, FAQ
   ```

---

**Status:** ✅ Both documentation tracks complete and complementary  
**Cross-referencing:** Ready to implement  
**Organization:** Professional & maintainable

