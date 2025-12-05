using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB;

/// <summary>
/// JSON serializer context for SharpCoreDB to support Native AOT compilation and source generation features in .NET 5+.
/// Required for .NET 10 when reflection-based serialization is disabled.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<Dictionary<string, object>>))]
[JsonSerializable(typeof(Table))]
[JsonSerializable(typeof(List<Table>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(object))]
public partial class SharpCoreDBJsonContext : JsonSerializerContext
{
}
