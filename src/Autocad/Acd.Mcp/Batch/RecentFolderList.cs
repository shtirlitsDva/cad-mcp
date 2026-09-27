using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Acd.Mcp.Batch
{
    // The BATCH palette's recent folders: newest first, no duplicates, at most
    // Capacity. Each change saves at once. AutoCAD-free.
    //
    // The list never checks whether its folders exist: on a network folder
    // that does not answer, that check blocks for minutes.
    public sealed class RecentFolderList
    {
        public const int Capacity = 20;

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Acd.Mcp",
            "recent-folders.json");

        private readonly string _path;
        private readonly List<string> _items;

        private RecentFolderList(string path, List<string> items)
        {
            _path = path;
            _items = items;
        }

        public IReadOnlyList<string> Items => _items;

        // A missing file is an empty list. A file that cannot be read is also
        // an empty list, and the next change overwrites it (the user's
        // decision: a recent list is not worth an error).
        public static RecentFolderList Load(string path)
        {
            List<string> items;
            try
            {
                items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                items = new();
            }
            return new RecentFolderList(path, items.Take(Capacity).ToList());
        }

        public void Add(string folder)
        {
            var normal = Normalize(folder);
            _items.RemoveAll(f => Same(f, normal));
            _items.Insert(0, normal);
            if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
            Save();
        }

        public void Remove(string folder)
        {
            if (_items.RemoveAll(f => Same(f, Normalize(folder))) > 0) Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items));
        }

        private static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(folder);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
