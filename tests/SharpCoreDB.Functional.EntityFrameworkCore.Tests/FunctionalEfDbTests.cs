using Microsoft.EntityFrameworkCore;
using SharpCoreDB.Functional.EntityFrameworkCore;

namespace SharpCoreDB.Functional.EntityFrameworkCore.Tests;

public sealed class FunctionalEfDbTests
{
    [Fact]
    public async Task GetByIdAsync_WhenEntityExists_ReturnsSome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var db = CreateContext();
        db.Users.Add(new UserEntity { Id = 1, Name = "Ada" });
        await db.SaveChangesAsync(cancellationToken);

        var sut = db.Functional();
        var result = await sut.GetByIdAsync<UserEntity>([1], cancellationToken);

        Assert.True(result.IsSome);
    }

    [Fact]
    public async Task UpdateAsync_WhenEntityExists_UpdatesPersistedValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var db = CreateContext();
        db.Users.Add(new UserEntity { Id = 2, Name = "Before" });
        await db.SaveChangesAsync(cancellationToken);

        var sut = db.Functional();
        var entity = await db.Users.FirstAsync(x => x.Id == 2, cancellationToken);
        entity.Name = "After";

        var result = await sut.UpdateAsync(entity, cancellationToken);
        var reloaded = await db.Users.FirstAsync(x => x.Id == 2, cancellationToken);

        Assert.True(result.IsSucc);
        Assert.Equal("After", reloaded.Name);
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ReturnsExpectedCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var db = CreateContext();
        db.Users.AddRange(
            new UserEntity { Id = 1, Name = "A" },
            new UserEntity { Id = 2, Name = "B" },
            new UserEntity { Id = 3, Name = "C" });
        await db.SaveChangesAsync(cancellationToken);

        var sut = db.Functional();
        var count = await sut.CountAsync<UserEntity>(x => x.Id >= 2, cancellationToken);

        Assert.Equal(2L, count);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"functional_ef_{Guid.NewGuid():N}")
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<UserEntity> Users => Set<UserEntity>();
    }

    private sealed class UserEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
