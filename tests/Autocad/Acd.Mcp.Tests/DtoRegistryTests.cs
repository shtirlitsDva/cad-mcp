using Xunit;
using Acd.Mcp.Serialization;

namespace Acd.Mcp.Tests;

public class DtoRegistryTests
{
    private sealed class Foo { public int X { get; init; } }

    private static object? Project(DtoRegistry r, Foo value)
    {
        Assert.True(r.TryGet(typeof(Foo), out var projection));
        return projection.Project(value);
    }

    [Fact]
    public void Register_then_TryGet_returns_projection()
    {
        var r = new DtoRegistry();
        r.Register<Foo>(f => f.X, DtoLayer.System, source: "system:foo.csx");

        Assert.Contains(typeof(Foo), r.RegisteredTypes);
        Assert.Equal(7, Project(r, new Foo { X = 7 }));
    }

    [Fact]
    public void User_layer_wins_when_it_is_registered_last()
    {
        var r = new DtoRegistry();
        r.Register<Foo>(f => "system", DtoLayer.System, source: "system:foo.csx");
        r.Register<Foo>(f => "user", DtoLayer.User, source: "user:foo.csx");

        Assert.Equal("user", Project(r, new Foo()));
        Assert.Single(r.RegisteredTypes);
    }

    // An incremental refresh compiles only the files that changed. A changed
    // system file then registers after the user file for the same type.
    [Fact]
    public void User_layer_wins_when_the_system_layer_is_registered_last()
    {
        var r = new DtoRegistry();
        r.Register<Foo>(f => "user", DtoLayer.User, source: "user:foo.csx");
        r.Register<Foo>(f => "system", DtoLayer.System, source: "system:foo.csx");

        Assert.Equal("user", Project(r, new Foo()));
    }

    [Fact]
    public void Registering_the_same_layer_again_replaces_it()
    {
        var r = new DtoRegistry();
        r.Register<Foo>(f => "old", DtoLayer.System, source: "system:foo.csx");
        r.Register<Foo>(f => "new", DtoLayer.System, source: "system:foo.csx");

        Assert.Equal("new", Project(r, new Foo()));
    }

    [Fact]
    public void Clear_empties_registry()
    {
        var r = new DtoRegistry();
        r.Register<Foo>(f => f.X, DtoLayer.User, source: "user:foo.csx");
        r.Clear();

        Assert.Empty(r.RegisteredTypes);
        Assert.False(r.TryGet(typeof(Foo), out _));
    }
}
