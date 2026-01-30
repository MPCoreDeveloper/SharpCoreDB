import re

# Read the file
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix 1: ReadString bounds check
content = re.sub(
    r'(private static string ReadString\(ReadOnlySpan<byte> data, ref int offset\)\s*\{\s*var length = ReadInt32\(data, ref offset\);)',
    r'\1\n            if (length < 0 || offset + length > data.Length)\n                throw new InvalidOperationException($"String truncated at offset {offset}: length={length}, remaining={data.Length - offset}");',
    content
)

# Fix 2: ReadBytes bounds check
content = re.sub(
    r'(private static byte\[\] ReadBytes\(ReadOnlySpan<byte> data, ref int offset\)\s*\{\s*var length = ReadInt32\(data, ref offset\);)',
    r'\1\n            if (length < 0 || offset + length > data.Length)\n                throw new InvalidOperationException($"Bytes truncated at offset {offset}: length={length}, remaining={data.Length - offset}");',
    content
)

# Fix 3: ReadGuid bounds check
content = re.sub(
    r'(private static Guid ReadGuid\(ReadOnlySpan<byte> data, ref int offset\)\s*\{)',
    r'\1\n            if (offset + 16 > data.Length)\n                throw new InvalidOperationException($"Guid truncated at offset {offset}: need 16 bytes, have {data.Length - offset}");',
    content
)

# Fix 4: ReadInt32 bounds check
content = re.sub(
    r'(private static int ReadInt32\(ReadOnlySpan<byte> data, ref int offset\)\s*\{)',
    r'\1\n            if (offset + 4 > data.Length)\n                throw new InvalidOperationException($"Int32 truncated at offset {offset}: need 4 bytes, have {data.Length - offset}");',
    content
)

# Fix 5: ReadInt64 bounds check
content = re.sub(
    r'(private static long ReadInt64\(ReadOnlySpan<byte> data, ref int offset\)\s*\{)',
    r'\1\n            if (offset + 8 > data.Length)\n                throw new InvalidOperationException($"Int64 truncated at offset {offset}: need 8 bytes, have {data.Length - offset}");',
    content
)

# Fix 6: ReadBoolean bounds check
content = re.sub(
    r'(private static bool ReadBoolean\(ReadOnlySpan<byte> data, ref int offset\)\s*\{)',
    r'\1\n            if (offset + 1 > data.Length)\n                throw new InvalidOperationException($"Boolean truncated at offset {offset}: need 1 byte, have {data.Length - offset}");',
    content
)

# Write the fixed file
with open('src/SharpCoreDB/DatabaseExtensions.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Bounds checking added to all Read methods successfully!")
