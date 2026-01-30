#!/usr/bin/env python3
"""
SharpCoreDB Serialization Visualizer
====================================

This tool visualizes how SharpCoreDB serializes records to binary format.
Useful for understanding the serialization process.

Usage:
    python3 visualize_serialization.py

Author: SharpCoreDB Documentation
License: MIT
"""

import struct
import json
from typing import Any, Dict, List, Tuple
from dataclasses import dataclass
from enum import IntEnum
from io import BytesIO


class BinaryTypeMarker(IntEnum):
    """Type markers used in binary serialization."""
    Null = 0
    Int32 = 1
    Int64 = 2
    Double = 3
    Boolean = 4
    DateTime = 5
    String = 6
    Bytes = 7
    Decimal = 8


@dataclass
class SerializationStep:
    """Represents one serialization step."""
    offset: int
    size: int
    value: Any
    description: str
    hex_bytes: str = ""


class BinaryRowSerializer:
    """Python implementation of SharpCoreDB's BinaryRowSerializer."""

    @staticmethod
    def serialize(row: Dict[str, Any]) -> Tuple[bytes, List[SerializationStep]]:
        """
        Serializes a row to binary format.
        Returns (binary_data, visualization_steps).
        """
        steps: List[SerializationStep] = []
        buffer = BytesIO()

        # Step 1: Write column count
        column_count = len(row)
        buffer.write(struct.pack('<I', column_count))
        steps.append(SerializationStep(
            offset=0,
            size=4,
            value=column_count,
            description=f"ColumnCount = {column_count}",
            hex_bytes=BinaryRowSerializer._bytes_to_hex(
                struct.pack('<I', column_count)
            )
        ))

        # Step 2: Write each column
        offset = 4
        for name, value in row.items():
            # Write column name
            name_bytes = name.encode('utf-8')
            buffer.write(struct.pack('<I', len(name_bytes)))
            
            steps.append(SerializationStep(
                offset=offset,
                size=4,
                value=len(name_bytes),
                description=f"NameLength (column '{name}') = {len(name_bytes)}",
                hex_bytes=BinaryRowSerializer._bytes_to_hex(
                    struct.pack('<I', len(name_bytes))
                )
            ))
            offset += 4

            buffer.write(name_bytes)
            steps.append(SerializationStep(
                offset=offset,
                size=len(name_bytes),
                value=name,
                description=f"ColumnName = '{name}' (UTF-8)",
                hex_bytes=BinaryRowSerializer._bytes_to_hex(name_bytes)
            ))
            offset += len(name_bytes)

            # Write type and value
            type_marker, value_bytes = BinaryRowSerializer._encode_value(value)
            buffer.write(bytes([type_marker]))
            
            steps.append(SerializationStep(
                offset=offset,
                size=1,
                value=BinaryRowSerializer._get_type_name(type_marker),
                description=f"TypeMarker = {type_marker} ({BinaryRowSerializer._get_type_name(type_marker)})",
                hex_bytes=BinaryRowSerializer._bytes_to_hex(bytes([type_marker]))
            ))
            offset += 1

            buffer.write(value_bytes)
            
            # Format value description
            if type_marker == BinaryTypeMarker.String:
                value_desc = f"String value = '{value}' ({len(value_bytes)-4} bytes UTF-8)"
            elif type_marker == BinaryTypeMarker.Bytes:
                value_desc = f"Bytes = {len(value_bytes)-4} bytes"
            else:
                value_desc = f"Value = {value}"

            steps.append(SerializationStep(
                offset=offset,
                size=len(value_bytes),
                value=value,
                description=value_desc,
                hex_bytes=BinaryRowSerializer._bytes_to_hex(value_bytes)
            ))
            offset += len(value_bytes)

        return buffer.getvalue(), steps

    @staticmethod
    def _encode_value(value: Any) -> Tuple[int, bytes]:
        """Encodes a value to (type_marker, binary_bytes)."""
        if value is None:
            return BinaryTypeMarker.Null, b''

        elif isinstance(value, bool):
            return BinaryTypeMarker.Boolean, bytes([1 if value else 0])

        elif isinstance(value, int):
            if -2147483648 <= value <= 2147483647:
                return BinaryTypeMarker.Int32, struct.pack('<i', value)
            else:
                return BinaryTypeMarker.Int64, struct.pack('<q', value)

        elif isinstance(value, float):
            return BinaryTypeMarker.Double, struct.pack('<d', value)

        elif isinstance(value, str):
            encoded = value.encode('utf-8')
            return BinaryTypeMarker.String, (
                struct.pack('<I', len(encoded)) + encoded
            )

        elif isinstance(value, bytes):
            return BinaryTypeMarker.Bytes, (
                struct.pack('<I', len(value)) + value
            )

        else:
            # Default: convert to string
            return BinaryRowSerializer._encode_value(str(value))

    @staticmethod
    def _get_type_name(marker: int) -> str:
        """Gets human-readable type name."""
        names = {
            0: "Null",
            1: "Int32",
            2: "Int64",
            3: "Double",
            4: "Boolean",
            5: "DateTime",
            6: "String",
            7: "Bytes",
            8: "Decimal",
        }
        return names.get(marker, "Unknown")

    @staticmethod
    def _bytes_to_hex(data: bytes) -> str:
        """Converts bytes to hex string."""
        return ' '.join(f'{b:02X}' for b in data)


def print_visualization(row: Dict[str, Any]):
    """Prints a nice visualization of serialization."""
    binary_data, steps = BinaryRowSerializer.serialize(row)

    print("=" * 100)
    print("SharpCoreDB Record Serialization Visualization")
    print("=" * 100)
    print()

    print(f"📋 Input Record:")
    print(f"   {json.dumps(row, indent=2)}")
    print()

    print(f"📊 Serialization Steps ({len(steps)} total):")
    print("-" * 100)
    print(f"{'Offset':>6} {'Size':>4} {'Type':>12} {'Value':>20} {'Hex Bytes':<40} {'Description':<40}")
    print("-" * 100)

    for step in steps:
        value_str = str(step.value)[:20].ljust(20)
        type_name = "Column" if "NameLength" in step.description else "Type/Value"
        
        print(f"{step.offset:6d} {step.size:4d} {type_name:>12} {value_str} "
              f"{step.hex_bytes:<40} {step.description:<40}")

    print("-" * 100)
    print()

    print(f"✅ Final Binary Data:")
    print(f"   Total Size: {len(binary_data)} bytes")
    print(f"   Hex Dump:")
    
    # Pretty-print hex
    for i in range(0, len(binary_data), 16):
        chunk = binary_data[i:i+16]
        hex_part = ' '.join(f'{b:02X}' for b in chunk)
        ascii_part = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
        print(f"     {i:04X}: {hex_part:<48} │{ascii_part}│")

    print()
    print("=" * 100)
    print()


def example_1_simple_types():
    """Example 1: Simple types (int, string, boolean)."""
    print("\n🔷 EXAMPLE 1: Simple Types")
    print("=" * 100)
    
    row = {
        "UserId": 42,
        "Name": "John Doe",
        "Active": True,
    }
    
    print_visualization(row)


def example_2_unicode_strings():
    """Example 2: Unicode string handling."""
    print("\n🔶 EXAMPLE 2: Unicode String Handling")
    print("=" * 100)
    
    row = {
        "City": "Café",              # Latin extended
        "Country": "日本",            # Japanese (3 chars, 9 bytes UTF-8)
        "Emoji": "🚀",               # Emoji (1 char, 4 bytes UTF-8)
    }
    
    print_visualization(row)


def example_3_large_strings():
    """Example 3: Large string handling."""
    print("\n🔴 EXAMPLE 3: Large String (No Fixed Length!)")
    print("=" * 100)
    
    large_text = "A" * 1000
    row = {
        "Id": 1,
        "LargeText": large_text,
    }
    
    binary_data, _ = BinaryRowSerializer.serialize(row)
    print(f"Input:")
    print(f"  Id: 1")
    print(f"  LargeText: {large_text[:50]}... (1000 characters)")
    print()
    print(f"Output:")
    print(f"  Total binary size: {len(binary_data)} bytes")
    print(f"  Breakdown:")
    print(f"    - ColumnCount: 4 bytes")
    print(f"    - Column 1 name ('Id'): 4 + 2 = 6 bytes")
    print(f"    - Column 1 value (42): 1 + 4 = 5 bytes")
    print(f"    - Column 2 name ('LargeText'): 4 + 9 = 13 bytes")
    print(f"    - Column 2 value (type + length + string): 1 + 4 + 1000 = 1005 bytes")
    print(f"    - Total: 4 + 6 + 5 + 13 + 1005 = {len(binary_data)} bytes")
    print()
    print("✅ NO FIXED LENGTH OVERHEAD! Only actual bytes used.")
    print()


def example_4_null_handling():
    """Example 4: NULL value handling."""
    print("\n🟦 EXAMPLE 4: NULL Value Handling")
    print("=" * 100)
    
    row = {
        "Id": 1,
        "OptionalField": None,
        "Name": "Test",
    }
    
    print_visualization(row)


def example_5_free_space_illustration():
    """Illustrates free space management (not actual serialization)."""
    print("\n📦 EXAMPLE 5: Free Space Management")
    print("=" * 100)
    print()
    
    print("SharpCoreDB File Layout (Illustration):")
    print()
    print("┌──────────────────────────────────────────────────────────┐")
    print("│ [Header: 512 bytes]                                      │")
    print("├──────────────────────────────────────────────────────────┤")
    print("│ [Block Registry: Variable]                               │")
    print("├──────────────────────────────────────────────────────────┤")
    print("│ [Free Space Map: Variable]                               │")
    print("├──────────────────────────────────────────────────────────┤")
    print("│ [Write-Ahead Log: Variable]                              │")
    print("├──────────────────────────────────────────────────────────┤")
    print("│ [Table Directory: Variable]                              │")
    print("├──────────────────────────────────────────────────────────┤")
    print("│ [Data Pages: Variable]                                   │")
    print("│  ├─ [Record 1: 50 bytes]     (allocated)                │")
    print("│  ├─ [Record 2: 100 bytes]    (allocated)                │")
    print("│  ├─ [Free: 4046 bytes]       (FSM marks as free)        │")
    print("│  ├─ [Record 3: 75 bytes]     (allocated)                │")
    print("│  └─ [Free: 4021 bytes]       (FSM marks as free)        │")
    print("└──────────────────────────────────────────────────────────┘")
    print()
    print("✅ Key Points:")
    print("  1. No pre-allocated free space in records")
    print("  2. Actual used bytes stored (50, 100, 75)")
    print("  3. Free space managed by FSM")
    print("  4. File grows exponentially (10MB → 20MB → 40MB)")
    print("  5. ZERO waste from variable-length strings!")
    print()


def main():
    """Run all examples."""
    print("\n" + "=" * 100)
    print("SharpCoreDB Serialization Visualizer - Complete Examples")
    print("=" * 100)

    example_1_simple_types()
    example_2_unicode_strings()
    example_3_large_strings()
    example_4_null_handling()
    example_5_free_space_illustration()

    print("\n" + "=" * 100)
    print("Summary:")
    print("=" * 100)
    print("""
✅ Variable-length strings = NO FIXED SIZE OVERHEAD
✅ UTF-8 encoding = Full Unicode support (Café, 日本, 🚀)
✅ Type markers = Self-describing format (no schema needed in binary)
✅ Length prefixes = Know exactly where columns/records end
✅ FSM allocation = Automatic free space management
✅ No waste = Only store actual bytes

🎯 Conclusion: The person saying "you need lots of free space" is WRONG!
   SharpCoreDB is optimized for variable-length data with zero waste.
""")
    print("=" * 100)


if __name__ == "__main__":
    main()
