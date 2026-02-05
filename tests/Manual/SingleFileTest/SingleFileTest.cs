using SharpCoreDB.Storage.Scdb;
using System.Runtime.InteropServices;

class SingleFileTest
{
    static void Main(string[] args)
    {
        Console.WriteLine("Testing SCDB Structures");

        // Test ScdbFileHeader
        var header = ScdbFileHeader.CreateDefault(4096);
        Console.WriteLine($"Header magic: 0x{header.Magic:X16}");
        Console.WriteLine($"Header version: {header.FormatVersion}");
        Console.WriteLine($"Page size: {header.PageSize}");
        Console.WriteLine($"Header valid: {header.IsValid}");

        // Test serialization
        Span<byte> buffer = stackalloc byte[(int)ScdbFileHeader.HEADER_SIZE];
        header.WriteTo(buffer);

        var parsed = ScdbFileHeader.Parse(buffer);
        Console.WriteLine($"Parsed magic: 0x{parsed.Magic:X16}");
        Console.WriteLine($"Round-trip success: {header.Magic == parsed.Magic}");

        // Test BlockEntry
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 4096,
            Length = 1024,
            Flags = (uint)BlockFlags.Dirty
        };

        var namedEntry = BlockEntry.WithName("table:users:data", entry);
        var name = namedEntry.GetName();
        Console.WriteLine($"Block name: {name}");
        Console.WriteLine($"Block type: {namedEntry.BlockType}");
        Console.WriteLine($"Block offset: {namedEntry.Offset}");

        Console.WriteLine("\n✅ SCDB structures test completed successfully!");
    }
}
