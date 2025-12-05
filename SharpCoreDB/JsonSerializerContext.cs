using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB;

/// <summary>
/// JSON serializer context for SharpCoreDB to support Native AOT and .NET 10 source generation.
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
