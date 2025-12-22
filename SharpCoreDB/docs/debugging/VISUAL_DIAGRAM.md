# Serialization Error - Visual Diagram

## The Problem (Before Fix)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ WRITE PHASE (WriteTypedValueToSpan)                                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Column: id (INTEGER = 20)                                              │
│ Writes: [NULL_FLAG:1][INT32:4] = 5 bytes total                         │
│ ┌─────────┬──────────────────┐                                         │
│ │ 01      │ 14 00 00 00      │                                         │
│ │ 1 byte  │ 4 bytes          │                                         │
│ └─────────┴──────────────────┘                                         │
│                                                                          │
│ Column: name (STRING = "User0")                                        │
│ Writes: [NULL_FLAG:1][LENGTH:4][STRING:5] = 10 bytes total             │
│ ┌─────────┬──────────────────┬─────────────────────┐                  │
│ │ 01      │ 05 00 00 00      │ 55 73 65 72 30      │                  │
│ │ 1 byte  │ 4 bytes (len=5)  │ 5 bytes ("User0")   │                  │
│ └─────────┴──────────────────┴─────────────────────┘                  │
│                                                                          │
│ ACTUAL BUFFER CONTENT: [01|14 00 00 00|01|05 00 00 00|55 73 65 72 30]  │
│ ACTUAL SIZE WRITTEN:   5 + 10 = 15 bytes                               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ BUFFER ALLOCATION (EstimateRowSize) - ❌ BROKEN                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Column: id (INTEGER)                                                   │
│ Allocates: 4 bytes (NO NULL FLAG!)                                    │
│ ┌──────────────────┐                                                   │
│ │ XXXXXXXXXXX      │                                                   │
│ │ 4 bytes reserved │                                                   │
│ └──────────────────┘                                                   │
│                                                                          │
│ Column: name (STRING)                                                  │
│ Allocates: 4 + 5 = 9 bytes (NO NULL FLAG!)                            │
│ ┌──────────────────────────────┐                                       │
│ │ XXXXXXXXXXXXXXXXXXX          │                                       │
│ │ 9 bytes reserved             │                                       │
│ └──────────────────────────────┘                                       │
│                                                                          │
│ TOTAL ALLOCATED: 4 + 9 = 13 bytes ← TOO SMALL!                        │
│ ACTUAL WRITTEN:  5 + 10 = 15 bytes ← OVERFLOW!                        │
│                                                                          │
│ OVERFLOW = 15 - 13 = 2 bytes overflow into next memory!               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ MEMORY CORRUPTION                                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Buffer Layout (13 bytes allocated):                                   │
│ ┌──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐                              │
│ │0 │1 │2 │3 │4 │5 │6 │7 │8 │9 │10│11│12│ (indices)                  │
│ └──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┘                              │
│                                                                          │
│ Write sequence (15 bytes needed):                                     │
│ [01|14 00 00 00] → offset 0-4 (5 bytes)      ✓ Within bounds         │
│ [01|05 00 00 00 55 73 65 72 30] → offset 5-14 (10 bytes)             │
│                                  ✗ OVERFLOWS! (buffer ends at 12)    │
│                                                                          │
│ After write:                                                           │
│ ┌──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐                              │
│ │01│14│00│00│00│01│05│00│00│00│55│73│65│  (+ 2 bytes overflow!)     │
│ │ID│   ID   │NA│  LENGTH   │  US (corrupted)                          │
│ └──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┘                              │
│  ↑ (null+int)                                                          │
│                                                                          │
│ Next column's data partially overwritten!                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ READ PHASE (ReadTypedValueFromSpan) - ❌ FAILS                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Read id (INTEGER):                                                     │
│   offset=0, reads [01|14 00 00 00] → id = 20 ✓                        │
│   bytesRead = 1 + 4 = 5                                                │
│   nextOffset = 0 + 5 = 5                                               │
│                                                                          │
│ Read name (STRING):                                                    │
│   offset=5, reads [01|05 00 00 00|55 73 65 72 30]                     │
│   Tries to get 5 bytes for "User0" → GARBAGE (overwritten!) ✗          │
│   Can't parse as valid string → EXCEPTION!                            │
│                                                                          │
│ Result: Deserialization FAILS with:                                   │
│   "Invalid String length: XXXXXXXX"                                   │
│   or                                                                    │
│   "Offset exceeds data length"                                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## The Solution (After Fix)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ BUFFER ALLOCATION (EstimateRowSize) - ✅ FIXED                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Column: id (INTEGER)                                                   │
│ Allocates: 1 (NULL FLAG) + 4 (VALUE) = 5 bytes ✓                      │
│                                                                          │
│ Column: name (STRING)                                                  │
│ Allocates: 1 (NULL FLAG) + 4 (LENGTH) + 5 (STRING) = 10 bytes ✓       │
│                                                                          │
│ TOTAL ALLOCATED: 5 + 10 = 15 bytes                                     │
│ ACTUAL WRITTEN:  5 + 10 = 15 bytes ✓ PERFECT MATCH                    │
│                                                                          │
│ No overflow! No corruption!                                            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ MEMORY LAYOUT                                                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Buffer Layout (15 bytes allocated):                                   │
│ ┌──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐                       │
│ │0 │1 │2 │3 │4 │5 │6 │7 │8 │9 │10│11│12│13│14│ (indices)           │
│ └──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┘                       │
│                                                                          │
│ Write sequence (15 bytes needed):                                     │
│ [01|14 00 00 00] → offset 0-4 (5 bytes)      ✓ Within bounds         │
│ [01|05 00 00 00 55 73 65 72 30] → offset 5-14 (10 bytes) ✓           │
│                                   Within bounds perfectly!             │
│                                                                          │
│ After write:                                                           │
│ ┌──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┐                       │
│ │01│14│00│00│00│01│05│00│00│00│55│73│65│72│30│                       │
│ │ID │  ID value   │NA│  LEN   │  "User0"    │                         │
│ └──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┴──┘                       │
│  ↑ (null+int)  ↑ (null+len+str)                                       │
│                                                                          │
│ Data intact! No corruption!                                           │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ READ PHASE (ReadTypedValueFromSpan) - ✅ SUCCEEDS                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Read id (INTEGER):                                                     │
│   offset=0, reads [01|14 00 00 00]                                     │
│   id = 20 ✓                                                            │
│   bytesRead = 1 + 4 = 5                                                │
│   nextOffset = 0 + 5 = 5                                               │
│                                                                          │
│ Read name (STRING):                                                    │
│   offset=5, reads [01|05 00 00 00|55 73 65 72 30]                     │
│   Gets correct 5 bytes = "User0" ✓                                     │
│   bytesRead = 1 + 4 + 5 = 10                                           │
│   nextOffset = 5 + 10 = 15                                             │
│                                                                          │
│ All data deserialized correctly!                                       │
│ Result: {"id": 20, "name": "User0"} ✓                                  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## The DateTime Bug

```
┌─────────────────────────────────────────────────────────────────────────┐
│ DATETIME OFFSET TRACKING BUG                                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ Before fix (bytesRead missing increment):                              │
│                                                                          │
│ case DataType.DateTime:                                                │
│     bytesRead = 1  (initialized for null flag)                         │
│     // ❌ MISSING: bytesRead += 8;                                      │
│     return DateTime.FromBinary(binaryValue);                           │
│                                                                          │
│ Result:                                                                │
│   bytesRead = 1 (should be 1 + 8 = 9!)                                 │
│   nextOffset = currentOffset + 1 (WRONG! Should +9)                    │
│   Next column reads from wrong position!                               │
│                                                                          │
├─────────────────────────────────────────────────────────────────────────┤
│ After fix (bytesRead properly incremented):                            │
│                                                                          │
│ case DataType.DateTime:                                                │
│     bytesRead = 1  (initialized for null flag)                         │
│     bytesRead += 8;  // ✅ ADDED                                        │
│     return DateTime.FromBinary(binaryValue);                           │
│                                                                          │
│ Result:                                                                │
│   bytesRead = 1 + 8 = 9 ✓                                              │
│   nextOffset = currentOffset + 9 ✓                                     │
│   Next column reads from correct position!                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Why This Broke Everything

```
Cascade Effect:

Column 1 (STRING): offset mismatch        ✓ Read correctly
                      ↓
Column 2 (DATETIME): missing bytesRead += 8  ✗ Offset tracking fails
                      ↓
Column 3+: all read from wrong offsets    ✗ ALL BROKEN
                      ↓
Result: Deserialization fails on any column after DateTime

Example with 6 columns:
  1. INTEGER ✓
  2. STRING ✓
  3. DATETIME ✗ (missing bytesRead)
  4. DECIMAL ✗ (wrong offset due to #3)
  5. STRING ✗ (wrong offset due to #3-4)
  6. DATE ✗ (wrong offset due to #3-5)

User sees error on column 4-6, blames DATETIME.
But root cause is the cascade from missing offset tracking!
```

---

## Summary

```
BEFORE FIX:
  ┌────────────────────────────────────────────┐
  │ EstimateRowSize: Forgets null flag bytes   │
  │         ↓                                   │
  │ Buffer too small                           │
  │         ↓                                   │
  │ WriteTypedValueToSpan overflows            │
  │         ↓                                   │
  │ Data corruption                            │
  │         ↓                                   │
  │ ReadTypedValueFromSpan reads garbage       │
  │         ↓                                   │
  │ Deserialization FAILS ❌                    │
  │         ↓                                   │
  │ "Serialization errors"                     │
  └────────────────────────────────────────────┘

AFTER FIX:
  ┌────────────────────────────────────────────┐
  │ EstimateRowSize: Includes null flag bytes  │
  │         ↓                                   │
  │ Buffer correctly sized                     │
  │         ↓                                   │
  │ WriteTypedValueToSpan fits perfectly       │
  │         ↓                                   │
  │ Data integrity preserved                   │
  │         ↓                                   │
  │ ReadTypedValueFromSpan reads correctly     │
  │         ↓                                   │
  │ Deserialization SUCCEEDS ✓                 │
  │         ↓                                   │
  │ Results returned correctly                 │
  └────────────────────────────────────────────┘
```
