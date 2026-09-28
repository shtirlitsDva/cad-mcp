using Acd.Mcp.Batch;
using Xunit;

namespace Acd.Mcp.Tests;

// BatchFileSelection.FindAsync is what both the palette's Refresh and the
// agent's autocad_batch_set_selection use to turn folder + mask into the file list.
public class BatchFileSelectionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "acd-mcp-selection-" + Guid.NewGuid().ToString("N"));

    // Releases the fake folder check of the timeout test, so its thread ends.
    private readonly ManualResetEventSlim _release = new();

    public BatchFileSelectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        foreach (var f in new[] { "b_SHT.dwg", "a_SHT.dwg", "notes.txt", "c.dwg", @"sub\d_SHT.dwg" })
            File.WriteAllText(Path.Combine(_root, f), "");
    }

    public void Dispose()
    {
        _release.Set();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string[] Names(IEnumerable<string> paths) => paths.Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task Find_ReturnsTheMatchingFilesSorted()
    {
        var scan = await BatchFileSelection.FindAsync(_root, "*_SHT.dwg", recurse: false);
        Assert.Equal(new[] { "a_SHT.dwg", "b_SHT.dwg" }, Names(scan.Matched));
    }

    // notes.txt is in the folder, but the tool works only with drawing files:
    // other files are never shown, not even as unmatched.
    [Fact]
    public async Task Find_ReturnsTheOtherDrawingFilesAsUnmatched()
    {
        var scan = await BatchFileSelection.FindAsync(_root, "*_SHT.dwg", recurse: false);
        Assert.Equal(new[] { "c.dwg" }, Names(scan.Unmatched));
    }

    [Fact]
    public async Task Find_WithRecurse_IncludesSubfolders()
    {
        var scan = await BatchFileSelection.FindAsync(_root, "*_SHT.dwg", recurse: true);
        Assert.Contains(Path.Combine(_root, "sub", "d_SHT.dwg"), scan.Matched);
        Assert.Equal(3, scan.Matched.Count);
    }

    // File Explorer order: 3 before 10. An ordinal sort puts 10 first.
    [Fact]
    public async Task Find_SortsNumbersNaturally()
    {
        foreach (var f in new[] { "10_SHT.dwg", "3_SHT.dwg", "10.dwg", "3.dwg" })
            File.WriteAllText(Path.Combine(_root, f), "");

        var scan = await BatchFileSelection.FindAsync(_root, "*_SHT.dwg", recurse: false);

        Assert.Equal(new[] { "3_SHT.dwg", "10_SHT.dwg", "a_SHT.dwg", "b_SHT.dwg" }, Names(scan.Matched));
        Assert.Equal(new[] { "3.dwg", "10.dwg", "c.dwg" }, Names(scan.Unmatched));
    }

    // A batch run opens each file with ReadDwgFile, which reads only drawing
    // formats. A mask such as *.* must not select anything else, and the
    // other files are not in Unmatched either.
    [Fact]
    public async Task Find_SeesOnlyDrawingFiles()
    {
        foreach (var f in new[] { "e.DWT", "f.dws", "g.bak", "h.dxf" })
            File.WriteAllText(Path.Combine(_root, f), "");

        var scan = await BatchFileSelection.FindAsync(_root, "*.*", recurse: false);

        Assert.Equal(new[] { "a_SHT.dwg", "b_SHT.dwg", "c.dwg", "e.DWT", "f.dws" }, Names(scan.Matched));
        Assert.Empty(scan.Unmatched);
    }

    // The user's case: *.bak selects nothing, and the .bak files do not show
    // as unmatched; the drawings do.
    [Fact]
    public async Task Find_WithAMaskForOtherFiles_SelectsNothingAndShowsTheDrawingsAsUnmatched()
    {
        File.WriteAllText(Path.Combine(_root, "c.bak"), "");

        var scan = await BatchFileSelection.FindAsync(_root, "*.bak", recurse: false);

        Assert.Empty(scan.Matched);
        Assert.Equal(new[] { "a_SHT.dwg", "b_SHT.dwg", "c.dwg" }, Names(scan.Unmatched));
    }

    [Fact]
    public async Task Find_InAFolderThatDoesNotExist_Throws()
    {
        var missing = Path.Combine(_root, "missing");
        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => BatchFileSelection.FindAsync(missing, "*.dwg", false));
        Assert.Equal($"Folder '{missing}' does not exist.", ex.Message);
    }

    // A network folder that does not answer blocks Directory.Exists for
    // minutes. The fake check blocks until the test ends.
    [Fact]
    public async Task Find_WhenTheFolderDoesNotAnswer_ThrowsWithinTheTimeLimit()
    {
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => BatchFileSelection.FindAsync(
            @"\\no-such-host\share", "*.dwg", false,
            folderTimeout: TimeSpan.FromMilliseconds(100),
            folderExists: _ => _release.Wait(TimeSpan.FromMinutes(1))));

        Assert.Equal(@"Folder '\\no-such-host\share' did not answer within 0.1 s. A network location may not be reachable.", ex.Message);
    }

    // A relative folder would resolve against AutoCAD's working directory,
    // which the agent cannot see.
    [Fact]
    public async Task Find_WithARelativeFolder_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => BatchFileSelection.FindAsync(@"drawings\batch", "*.dwg", false));
        Assert.Equal(@"Folder 'drawings\batch' is not an absolute path.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Find_WithAnEmptyMask_Throws(string mask)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => BatchFileSelection.FindAsync(_root, mask, false));
        Assert.Equal("The mask is empty. Give a file mask such as *.dwg or *_SHT.dwg.", ex.Message);
    }
}
