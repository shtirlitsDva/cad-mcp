using Acd.Mcp.Batch;
using Xunit;

namespace Acd.Mcp.Tests;

// The BATCH palette's recent-folders dropdown. Each change saves at once, so
// a crash or a reload loses nothing.
public class RecentFolderListTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "acd-mcp-recent-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "recent-folders.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_WhenTheFileIsMissing_GivesAnEmptyList()
    {
        Assert.Empty(RecentFolderList.Load(FilePath).Items);
    }

    [Fact]
    public void Add_PutsTheNewestFolderFirst()
    {
        var list = RecentFolderList.Load(FilePath);
        list.Add(@"C:\a");
        list.Add(@"C:\b");
        Assert.Equal(new[] { @"C:\b", @"C:\a" }, list.Items);
    }

    // Case and a trailing separator do not make a different folder on Windows.
    [Fact]
    public void Add_OfAFolderAlreadyInTheList_MovesItToTheTop()
    {
        var list = RecentFolderList.Load(FilePath);
        list.Add(@"C:\a");
        list.Add(@"C:\b");
        list.Add(@"c:\A\");
        Assert.Equal(new[] { @"c:\A", @"C:\b" }, list.Items);
    }

    [Fact]
    public void Add_KeepsOnlyTheNewestTwenty()
    {
        var list = RecentFolderList.Load(FilePath);
        for (var i = 1; i <= 25; i++) list.Add($@"C:\f{i}");

        Assert.Equal(RecentFolderList.Capacity, list.Items.Count);
        Assert.Equal(@"C:\f25", list.Items[0]);
        Assert.Equal(@"C:\f6", list.Items[^1]);
    }

    [Fact]
    public void Remove_TakesTheFolderOutOfTheList()
    {
        var list = RecentFolderList.Load(FilePath);
        list.Add(@"C:\a");
        list.Add(@"C:\b");
        list.Remove(@"C:\A");
        Assert.Equal(new[] { @"C:\b" }, list.Items);
    }

    [Fact]
    public void EachChange_IsSaved()
    {
        var list = RecentFolderList.Load(FilePath);
        list.Add(@"C:\a");
        list.Add(@"C:\b");
        list.Remove(@"C:\a");

        Assert.Equal(new[] { @"C:\b" }, RecentFolderList.Load(FilePath).Items);
    }

    // The user's decision: a file that cannot be read is rebuilt from nothing.
    [Fact]
    public void Load_OfAFileThatIsNotReadable_GivesAnEmptyListThatTheNextAddOverwrites()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var list = RecentFolderList.Load(FilePath);
        Assert.Empty(list.Items);

        list.Add(@"C:\a");
        Assert.Equal(new[] { @"C:\a" }, RecentFolderList.Load(FilePath).Items);
    }
}
