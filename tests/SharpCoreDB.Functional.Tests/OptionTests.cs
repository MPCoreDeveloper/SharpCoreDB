using SharpCoreDB.Functional;

namespace SharpCoreDB.Functional.Tests;

/// <summary>
/// Behavioural contract of <see cref="Option{T}"/>.
/// Added with the mutation-testing baseline: Stryker reported the null guard in
/// <see cref="Option{T}.Some(T)"/> and the async <c>Match</c> branch as surviving mutants
/// (verified by hand — the suite stayed green with the mutation applied). These cases pin
/// both behaviours, plus the rest of the public surface, so a regressed branch fails here.
/// </summary>
public sealed class OptionTests
{
    [Fact]
    public void Some_WithNullValue_ThrowsArgumentNullException()
    {
        // Act / Assert — the guard is the only thing separating Some(null) from a corrupt option
        Assert.Throws<ArgumentNullException>(() => Option<string>.Some(null!));
    }

    [Fact]
    public void Some_WithValue_ReportsIsSomeAndNotIsNone()
    {
        // Act
        var sut = Option<int>.Some(42);

        // Assert
        Assert.True(sut.IsSome);
        Assert.False(sut.IsNone);
    }

    [Fact]
    public void None_ReportsIsNoneAndNotIsSome()
    {
        // Act
        var sut = Option<int>.None;

        // Assert
        Assert.False(sut.IsSome);
        Assert.True(sut.IsNone);
    }

    [Fact]
    public void Match_WhenSome_InvokesSomeBranchOnly()
    {
        // Arrange
        var sut = Option<int>.Some(7);
        var noneCalled = false;

        // Act
        var result = sut.Match(Some: v => v * 2, None: () => { noneCalled = true; return -1; });

        // Assert
        Assert.Equal(14, result);
        Assert.False(noneCalled);
    }

    [Fact]
    public void Match_WhenNone_InvokesNoneBranchOnly()
    {
        // Arrange
        var sut = Option<int>.None;
        var someCalled = false;

        // Act
        var result = sut.Match(Some: v => { someCalled = true; return v; }, None: () => -1);

        // Assert
        Assert.Equal(-1, result);
        Assert.False(someCalled);
    }

    [Fact]
    public async Task MatchAsync_WhenSome_InvokesSomeBranchOnly()
    {
        // Arrange
        var sut = Option<int>.Some(7);
        var noneCalled = false;

        // Act
        var result = await sut.Match(
            Some: v => Task.FromResult($"some:{v}"),
            None: () => { noneCalled = true; return Task.FromResult("none"); });

        // Assert
        Assert.Equal("some:7", result);
        Assert.False(noneCalled);
    }

    [Fact]
    public async Task MatchAsync_WhenNone_InvokesNoneBranchOnly()
    {
        // Arrange — the async overload had no test at all before the mutation baseline
        var sut = Option<int>.None;
        var someCalled = false;

        // Act
        var result = await sut.Match(
            Some: v => { someCalled = true; return Task.FromResult($"some:{v}"); },
            None: () => Task.FromResult("none"));

        // Assert
        Assert.Equal("none", result);
        Assert.False(someCalled);
    }

    [Fact]
    public void IfSome_WhenSome_InvokesAction()
    {
        // Arrange
        var sut = Option<int>.Some(5);
        var seen = 0;

        // Act
        sut.IfSome(v => seen = v);

        // Assert
        Assert.Equal(5, seen);
    }

    [Fact]
    public void IfSome_WhenNone_DoesNotInvokeAction()
    {
        // Arrange
        var sut = Option<int>.None;
        var called = false;

        // Act
        sut.IfSome(_ => called = true);

        // Assert
        Assert.False(called);
    }

    [Fact]
    public void IfNone_WhenNone_InvokesAction()
    {
        // Arrange
        var sut = Option<int>.None;
        var called = false;

        // Act
        sut.IfNone(() => called = true);

        // Assert
        Assert.True(called);
    }

    [Fact]
    public void IfNone_WhenSome_DoesNotInvokeAction()
    {
        // Arrange
        var sut = Option<int>.Some(1);
        var called = false;

        // Act
        sut.IfNone(() => called = true);

        // Assert
        Assert.False(called);
    }

    [Fact]
    public void Map_WhenSome_ReturnsMappedValue()
    {
        // Act
        var sut = Option<int>.Some(4).Map(v => v.ToString());

        // Assert
        Assert.Equal("4", sut.Match(Some: s => s, None: () => "none"));
    }

    [Fact]
    public void Map_WhenNone_ReturnsNoneWithoutInvokingMap()
    {
        // Arrange
        var sut = Option<int>.None;
        var called = false;

        // Act
        var mapped = sut.Map(v => { called = true; return v; });

        // Assert
        Assert.True(mapped.IsNone);
        Assert.False(called);
    }

    [Fact]
    public void Bind_WhenSome_ReturnsBoundOption()
    {
        // Act
        var sut = Option<int>.Some(4).Bind(v => v > 0 ? Option<string>.Some("positive") : Option<string>.None);

        // Assert
        Assert.Equal("positive", sut.Match(Some: s => s, None: () => "none"));
    }

    [Fact]
    public void Bind_WhenNone_ReturnsNoneWithoutInvokingBind()
    {
        // Arrange
        var sut = Option<int>.None;
        var called = false;

        // Act
        var bound = sut.Bind(v => { called = true; return Option<int>.Some(v); });

        // Assert
        Assert.True(bound.IsNone);
        Assert.False(called);
    }

    [Fact]
    public void IfNoneDefault_WhenSome_ReturnsContainedValue()
    {
        // Act / Assert
        Assert.Equal(3, Option<int>.Some(3).IfNone(-1));
    }

    [Fact]
    public void IfNoneDefault_WhenNone_ReturnsDefaultValue()
    {
        // Act / Assert
        Assert.Equal(-1, Option<int>.None.IfNone(-1));
    }

    [Fact]
    public void IfNoneFactory_WhenSome_ReturnsValueWithoutInvokingFactory()
    {
        // Arrange
        var called = false;

        // Act
        var result = Option<int>.Some(3).IfNone(() => { called = true; return -1; });

        // Assert
        Assert.Equal(3, result);
        Assert.False(called);
    }


    [Fact]
    public void IfNoneFactory_WhenNone_ReturnsFactoryValue()
    {
        // Act / Assert — the factory overload must be the one invoked when empty
        Assert.Equal(-1, Option<int>.None.IfNone(() => -1));
    }

    [Fact]
    public void ImplicitConversion_FromNull_ProducesNone()
    {
        // Arrange
        string? value = null;

        // Act
        Option<string> sut = value;

        // Assert
        Assert.True(sut.IsNone);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSome()
    {
        // Act
        Option<string> sut = "abc";

        // Assert
        Assert.True(sut.IsSome);
        Assert.Equal("abc", sut.Match(Some: s => s, None: () => "none"));
    }

    [Fact]
    public void Equals_WhenBothSomeWithSameValue_IsTrue()
    {
        // Act / Assert
        Assert.True(Option<int>.Some(5).Equals(Option<int>.Some(5)));
        Assert.True(Option<int>.Some(5) == Option<int>.Some(5));
        Assert.False(Option<int>.Some(5) != Option<int>.Some(5));
    }

    [Fact]
    public void Equals_WhenBothSomeWithDifferentValues_IsFalse()
    {
        // Act / Assert
        Assert.False(Option<int>.Some(5).Equals(Option<int>.Some(6)));
        Assert.True(Option<int>.Some(5) != Option<int>.Some(6));
    }

    [Fact]
    public void Equals_WhenSomeVersusNone_IsFalse()
    {
        // Act / Assert
        Assert.False(Option<int>.Some(5).Equals(Option<int>.None));
        Assert.False(Option<int>.None.Equals(Option<int>.Some(5)));
        Assert.True(Option<int>.Some(5) != Option<int>.None);
    }

    [Fact]
    public void Equals_WhenBothNone_IsTrue()
    {
        // Act / Assert
        Assert.True(Option<int>.None.Equals(Option<int>.None));
        Assert.True(Option<int>.None == Option<int>.None);
    }

    [Fact]
    public void EqualsObject_WhenOtherTypeOrNull_IsFalse()
    {
        // Act / Assert
        Assert.False(Option<int>.Some(1).Equals((object?)null));
        Assert.False(Option<int>.Some(1).Equals("1"));
        Assert.True(Option<int>.Some(1).Equals((object)Option<int>.Some(1)));
    }

    [Fact]
    public void GetHashCode_WhenSameValue_IsEqual()
    {
        // Act / Assert
        Assert.Equal(Option<int>.Some(5).GetHashCode(), Option<int>.Some(5).GetHashCode());
        Assert.Equal(0, Option<int>.None.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WhenSome_ReflectsTheValue()
    {
        // Assert — Some(5) must not hash to the None sentinel (0)
        Assert.NotEqual(0, Option<int>.Some(5).GetHashCode());
    }

    [Fact]
    public void GetHashCode_WhenNone_IsZeroForReferenceTypeValuesToo()
    {
        // Assert — None must not hash the absent value (dereferencing it would throw)
        Assert.Equal(0, Option<string>.None.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WhenNone_IsZeroEvenWhenDefaultHashesNonZero()
    {
        // Assert — None is a sentinel; it must not depend on what default(T) happens to hash to
        Assert.Equal(0, Option<NonZeroHashValue>.None.GetHashCode());
    }

    /// <summary>
    /// Value type whose default instance hashes to a non-zero value, so a
    /// "hash the value even when empty" regression cannot hide behind null's zero hash.
    /// </summary>
    private readonly struct NonZeroHashValue : IEquatable<NonZeroHashValue>
    {
        public bool Equals(NonZeroHashValue other) => true;

        public override bool Equals(object? obj) => obj is NonZeroHashValue;

        public override int GetHashCode() => 12345;
    }

    [Fact]
    public void ToString_WhenSome_FormatsValue()
    {
        // Act / Assert — must describe the value, not merely its presence
        Assert.Equal("Some(5)", Option<int>.Some(5).ToString());
    }

    [Fact]
    public void ToString_WhenNone_ReturnsNone()
    {
        // Act / Assert
        Assert.Equal("None", Option<int>.None.ToString());
    }
}
