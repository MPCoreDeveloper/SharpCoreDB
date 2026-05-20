using System.Data;
using SharpCoreDB.Data.Provider;
using Xunit;

namespace SharpCoreDB.Provider.Sync.Tests;

public class SharpCoreDBCommandTests
{
    [Fact]
    public void Constructor_Default_ShouldInitializeDefaults()
    {
        var cmd = new SharpCoreDBCommand();
        Assert.Equal(CommandType.Text, cmd.CommandType);
        Assert.Equal(30, cmd.CommandTimeout);
        Assert.NotNull(cmd.Parameters);
    }

    [Fact]
    public void Constructor_WithCommandText_ShouldSetCommandText()
    {
        var cmd = new SharpCoreDBCommand("SELECT 1");
        Assert.Equal("SELECT 1", cmd.CommandText);
    }

    [Fact]
    public void Cancel_ShouldNotThrow()
    {
        var cmd = new SharpCoreDBCommand();
        cmd.Cancel(); // no-op, should not throw
    }
}
