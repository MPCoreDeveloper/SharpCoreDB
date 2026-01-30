#!/usr/bin/env python3
"""Add bounds checking to binary deserialization Read methods."""

def add_bounds_check(lines, start_pattern, check_code):
    """Add bounds checking after a pattern match."""
    i = 0
    while i < len(lines):
        if start_pattern in lines[i]:
            # Found the pattern - check if bounds check already exists
            if i + 1 < len(lines) and 'truncated at offset' in lines[i + 1]:
                print(f"Skipping {start_pattern} - already has bounds check")
                i += 1
                continue
            # Insert the check after the line
            lines.insert(i + 1, check_code)
            print(f"Added bounds check after: {lines[i].strip()}")
            i += 2  # Skip the newly inserted line
        else:
            i += 1
    return lines

# Read file
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Add bounds checks
lines = add_bounds_check(
    lines,
    'private static string ReadString(ReadOnlySpan<byte> data, ref int offset)',
    '        {\n            var length = ReadInt32(data, ref offset);\n            if (length < 0 || offset + length > data.Length)\n                throw new InvalidOperationException($"String truncated at offset {offset}: length={length}, remaining={data.Length - offset}");\n'
)

# For ReadString, we need a different approach - find the specific line pattern
i = 0
while i < len(lines):
    if 'private static string ReadString' in lines[i]:
        # Find the next line that has "var length = ReadInt32"
        j = i + 1
        while j < len(lines) and j < i + 10:  # Look ahead up to 10 lines
            if 'var length = ReadInt32(data, ref offset);' in lines[j]:
                # Check if next line already has bounds check
                if j + 1 < len(lines) and 'truncated at offset' not in lines[j + 1]:
                    # Insert bounds check
                    lines.insert(j + 1, '            if (length < 0 || offset + length > data.Length)\n')
                    lines.insert(j + 2, '                throw new InvalidOperationException($"String truncated at offset {offset}: length={length}, remaining={data.Length - offset}");\n')
                    print(f"Added ReadString bounds check at line {j}")
                break
            j += 1
    elif 'private static byte[] ReadBytes' in lines[i]:
        # Find the next line that has "var length = ReadInt32"
        j = i + 1
        while j < len(lines) and j < i + 10:
            if 'var length = ReadInt32(data, ref offset);' in lines[j]:
                if j + 1 < len(lines) and 'truncated at offset' not in lines[j + 1]:
                    lines.insert(j + 1, '            if (length < 0 || offset + length > data.Length)\n')
                    lines.insert(j + 2, '                throw new InvalidOperationException($"Bytes truncated at offset {offset}: length={length}, remaining={data.Length - offset}");\n')
                    print(f"Added ReadBytes bounds check at line {j}")
                break
            j += 1
    elif 'private static Guid ReadGuid' in lines[i] and '{' in lines[i + 1]:
        # Insert after the opening brace
        if 'truncated at offset' not in lines[i + 2]:
            lines.insert(i + 2, '            if (offset + 16 > data.Length)\n')
            lines.insert(i + 3, '                throw new InvalidOperationException($"Guid truncated at offset {offset}: need 16 bytes, have {data.Length - offset}");\n')
            print(f"Added ReadGuid bounds check at line {i + 2}")
    elif 'private static bool ReadBoolean' in lines[i] and '{' in lines[i + 1]:
        if 'truncated at offset' not in lines[i + 2]:
            lines.insert(i + 2, '            if (offset + 1 > data.Length)\n')
            lines.insert(i + 3, '                throw new InvalidOperationException($"Boolean truncated at offset {offset}: need 1 byte, have {data.Length - offset}");\n')
            print(f"Added ReadBoolean bounds check at line {i + 2}")
    elif 'private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)' in lines[i] and '{' in lines[i + 1]:
        if 'truncated at offset' not in lines[i + 2]:
            lines.insert(i + 2, '            if (offset + 4 > data.Length)\n')
            lines.insert(i + 3, '                throw new InvalidOperationException($"Int32 truncated at offset {offset}: need 4 bytes, have {data.Length - offset}");\n')
            print(f"Added ReadInt32 bounds check at line {i + 2}")
    elif 'private static long ReadInt64' in lines[i] and '{' in lines[i + 1]:
        if 'truncated at offset' not in lines[i + 2]:
            lines.insert(i + 2, '            if (offset + 8 > data.Length)\n')
            lines.insert(i + 3, '                throw new InvalidOperationException($"Int64 truncated at offset {offset}: need 8 bytes, have {data.Length - offset}");\n')
            print(f"Added ReadInt64 bounds check at line {i + 2}")
    i += 1

# Write back
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print("\n✅ All bounds checks added successfully!")
