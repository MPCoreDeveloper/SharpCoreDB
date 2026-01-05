using SharpCoreDB;
using System.Diagnostics;

namespace SharpCoreDB.DemoJoinsSubQ;

internal static class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("SharpCoreDB Join/Subquery Demo");
        var runner = new DemoRunner();
        runner.Run();
        Console.ReadLine();
    }
}
