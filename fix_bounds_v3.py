#!/usr/bin/env python3
"""Add bounds checking to binary deserialization Read methods - v3."""

def find_and_fix_method(lines, method_signature, bytes_needed):
    """Find a method and add bounds checking."""
    i = 0
    while i < len(lines):
        if method_signature in lines[i]:
            # Found the method - now find the opening brace
            brace_line = i + 1
            if '{' not in lines[brace_line]:
                brace_line = i
            
            # Check if bounds check already exists
            check_line = brace_line + 1
            if check_line < len(lines) and 'truncated at offset' in lines[check_line]:
                print(f"✓ {method_signature[:40]}... already has bounds check")
                return
            
            # Insert bounds check
            indent = '            '
            if bytes_needed == 'length':
                # For ReadString and ReadBytes - check after reading length
                # Find "var length = ReadInt32" line
                j = brace_line
                while j < min(len(lines), brace_line + 10):
                    if 'var length = ReadInt32(data, ref offset);' in lines[j]:
                        # Insert bounds check right after
                        lines.insert(j + 1, f'{indent}if (length < 0 || offset + length > data.Length)\n')
                        lines.insert(j + 2, f'{indent}    throw new InvalidOperationException($"Truncated at offset {{offset}}: length={{length}}, remaining={{data.Length - offset}}");\n')
                        print(f"✓ Added bounds check to {method_signature[:40]}...")
                        return
                    j += 1
            else:
                # For fixed-size reads - insert right after opening brace
                lines.insert(check_line, f'{indent}if (offset + {bytes_needed} > data.Length)\n')
                lines.insert(check_line + 1, f'{indent}    throw new InvalidOperationException($"Truncated at offset {{offset}}: need {bytes_needed} bytes, have {{data.Length - offset}}");\n')
                print(f"✓ Added bounds check to {method_signature[:40]}...")
                return
        i += 1
    print(f"✗ Could not find {method_signature[:40]}...")

# Read file
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()

print("Adding bounds checks...\n")

# Add bounds checks to each method
find_and_fix_method(lines, 'private static string ReadString(ReadOnlySpan<byte> data, ref int offset)', 'length')
find_and_fix_method(lines, 'private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset)', 'length')
find_and_fix_method(lines, 'private static Guid ReadGuid(ReadOnlySpan<byte> data, ref int offset)', 16)
find_and_fix_method(lines, 'private static bool ReadBoolean(ReadOnlySpan<byte> data, ref int offset)', 1)
find_and_fix_method(lines, 'private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)', 4)
find_and_fix_method(lines, 'private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)', 8)

# Write back
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print("\n✅ Done!")
