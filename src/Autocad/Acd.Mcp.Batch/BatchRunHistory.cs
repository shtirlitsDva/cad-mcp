using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acd.Mcp.Batch
{
    // Durable history of completed batch runs.
    //
    // Storage:
    //   %LOCALAPPDATA%\Acd.Mcp\batch-runs\<yyyy-MM-dd_HH-mm-ss>_<run_id>.json
    //
    // The filename embeds the timestamp so newest-first enumeration is
    // a string-sort on the file name — no metadata index file needed.
    //
    // Each file holds one BatchRunRecord (the same contract the pipe
    // carries). Load returns that record as stored.
    //
    // Pagination:
    //   ListRecent(limit, offset) returns at most `limit` entries, skipping
    //   the first `offset`. Default limit 20; max 100 — enforced so an
    //   unbounded response cannot flood the agent's context.
    public sealed class BatchRunHistory
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Enum-as-string so the file is human-readable.
            Converters = { new JsonStringEnumConverter() },
        };

        public const int DefaultLimit = 20;
        public const int MaxLimit = 100;

        public string Root { get; }

        public BatchRunHistory(string? rootOverride = null)
        {
            Root = rootOverride ?? DefaultRoot();
            Directory.CreateDirectory(Root);
        }

        // Returns the path of the written file.
        public string Save(BatchRunReport report)
        {
            var stamp = report.StartedAt.ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(Root, $"{stamp}_{report.RunId}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report.ToRecord(), JsonOptions));
            return path;
        }

        // Null when no run has this id. A file that cannot be read throws.
        public BatchRunRecord? Load(string runId)
        {
            var match = Directory.GetFiles(Root, $"*_{runId}.json").FirstOrDefault();
            return match is null ? null : Read(match);
        }

        // Null when the history is empty. The newest file that cannot be
        // read throws: an older run is not a stand-in for the newest one.
        public BatchRunRecord? LoadLast()
        {
            var newest = NewestFirst().FirstOrDefault();
            return newest is null ? null : Read(newest);
        }

        public RunPage ListRecent(int limit = DefaultLimit, int offset = 0)
        {
            limit = Math.Clamp(limit, 1, MaxLimit);
            offset = Math.Max(offset, 0);

            var files = NewestFirst();
            var entries = new List<RunSummary>();
            var unreadable = new List<UnreadableRun>();
            foreach (var f in files.Skip(offset).Take(limit))
            {
                try
                {
                    entries.Add(Summarize(Read(f)));
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    unreadable.Add(new UnreadableRun(Path.GetFileName(f), ex.Message));
                }
            }
            return new RunPage(limit, offset, files.Length, entries, unreadable);
        }

        private string[] NewestFirst()
        {
            var files = Directory.GetFiles(Root, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            Array.Reverse(files);
            return files;
        }

        private static BatchRunRecord Read(string path)
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BatchRunRecord>(stream, JsonOptions)
                ?? throw new JsonException($"'{Path.GetFileName(path)}' holds no batch run.");
        }

        private static RunSummary Summarize(BatchRunRecord r)
        {
            int pass = r.Results.Count(f => f.Status == FileOutcomeStatus.Pass);
            return new RunSummary(
                RunId: r.RunId,
                StartedAt: r.StartedAt,
                CompletedAt: r.CompletedAt,
                RequestedMode: r.RequestedMode,
                FileCount: r.Files.Count,
                PassCount: pass,
                FailureCount: r.Results.Count - pass,
                Cancelled: r.Cancelled,
                AbortedReason: r.AbortedReason);
        }

        private static string DefaultRoot() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Acd.Mcp",
            "batch-runs");
    }
}
