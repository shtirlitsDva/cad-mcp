using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Acd.Mcp.Batch
{
    // Matched: the drawings a batch run works on. Unmatched: the other
    // drawings in the same search, so the palette can show what the mask left
    // out. Files that are not drawings are in neither list.
    public sealed record BatchFileScan(IReadOnlyList<string> Matched, IReadOnlyList<string> Unmatched);

    // Folder + mask -> the files a batch run works on. Shared by the BATCH
    // palette's Refresh and the agent's autocad_batch_set_selection, so both
    // select the same files. AutoCAD-free.
    //
    // The disk work never runs on the caller's thread: the caller is the UI
    // thread or AutoCAD's main thread, and on a network folder that does not
    // answer, Directory.Exists blocks for minutes.
    public static class BatchFileSelection
    {
        // Long enough for a slow but reachable share; short enough that a dead
        // one fails while the user still waits for it.
        public static readonly TimeSpan FolderTimeout = TimeSpan.FromSeconds(5);

        // A batch run opens each file with Database.ReadDwgFile, which reads
        // only these formats. The tool never sees other files.
        private static readonly HashSet<string> DrawingExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".dwg", ".dwt", ".dws" };

        // Both lists in File Explorer order (3 before 10). Throws when the
        // input cannot select files: a relative folder (it would resolve
        // against AutoCAD's working directory), an empty mask, a folder that
        // does not exist, or a folder that does not answer within
        // FolderTimeout. An invalid mask throws from Directory.EnumerateFiles.
        public static Task<BatchFileScan> FindAsync(string folder, string mask, bool recurse, CancellationToken ct = default) =>
            FindAsync(folder, mask, recurse, FolderTimeout, Directory.Exists, ct);

        // Tests give a folder check that blocks, because a dead share cannot
        // be made in a unit test.
        internal static async Task<BatchFileScan> FindAsync(
            string folder, string mask, bool recurse,
            TimeSpan folderTimeout, Func<string, bool> folderExists, CancellationToken ct = default)
        {
            if (!Path.IsPathFullyQualified(folder))
                throw new ArgumentException($"Folder '{folder}' is not an absolute path.");
            if (string.IsNullOrWhiteSpace(mask))
                throw new ArgumentException("The mask is empty. Give a file mask such as *.dwg or *_SHT.dwg.");

            // Windows cannot cancel a blocked Directory.Exists. On a timeout
            // its thread is left to end by itself, so it gets a thread of its
            // own and not one of the thread pool's.
            var check = Task.Factory.StartNew(() => folderExists(folder),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            bool exists;
            try
            {
                exists = await check.WaitAsync(folderTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
                    "Folder '{0}' did not answer within {1:0.##} s. A network location may not be reachable.",
                    folder, folderTimeout.TotalSeconds));
            }
            if (!exists)
                throw new DirectoryNotFoundException($"Folder '{folder}' does not exist.");

            return await Task.Factory.StartNew(() => Scan(folder, mask, recurse),
                ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
        }

        private static BatchFileScan Scan(string folder, string mask, bool recurse)
        {
            var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var drawings = Directory.EnumerateFiles(folder, "*", option).Where(IsDrawing).ToList();
            var matched = Directory.EnumerateFiles(folder, mask, option)
                .Where(IsDrawing)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new BatchFileScan(
                Matched: drawings.Where(matched.Contains).Order(NaturalOrder.Instance).ToArray(),
                Unmatched: drawings.Where(p => !matched.Contains(p)).Order(NaturalOrder.Instance).ToArray());
        }

        private static bool IsDrawing(string path) => DrawingExtensions.Contains(Path.GetExtension(path));

        // The Windows shell's own compare, so the order is the one the user
        // sees in File Explorer.
        private sealed class NaturalOrder : IComparer<string>
        {
            public static readonly NaturalOrder Instance = new();

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            private static extern int StrCmpLogicalW(string x, string y);

            public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
        }
    }
}
