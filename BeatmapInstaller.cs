using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BEATNET;

internal sealed class BeatmapInstaller
{
    private const string RecordName = ".beatnet.json";
    private readonly string songs;
    private readonly string customSongs;
    private readonly string staging;

    internal BeatmapInstaller(string dataPath)
    {
        customSongs = Path.GetFullPath(Path.Combine(dataPath, "CustomSongs"));
        songs = Path.Combine(customSongs, "BEATNET");
        staging = Path.Combine(dataPath, "BEATNET", "staging");
    }

    internal string? InstalledRevision(string id)
    {
        if (!BeatNetClient.IsId(id))
        {
            return null;
        }
        try
        {
            var folder = Path.Combine(songs, id);
            CheckParents(folder);
            var path = Path.Combine(folder, RecordName);
            if (!File.Exists(path) || new FileInfo(path).Length > 65536)
            {
                return null;
            }
            var record = JsonConvert.DeserializeObject<InstalledBeatmap>(File.ReadAllText(path));
            return record?.Id == id && BeatNetClient.IsId(record.RevisionId) ? record.RevisionId : null;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
        {
            return null;
        }
    }

    internal Task Install(BeatNetClient client, BeatmapEntry beatmap, Action<string> progress, CancellationToken token, Action<float>? downloadProgress = null)
    {
        return Task.Run(() => InstallFiles(client, beatmap, progress, token, downloadProgress), token);
    }

    internal string? InstalledFolder(BeatmapEntry beatmap)
    {
        if (BeatNetClient.IsId(beatmap.Id))
        {
            return InstalledRevision(beatmap.Id) == null ? null : Path.Combine(songs, beatmap.Id);
        }
        if (beatmap.LocalPath.Length == 0)
        {
            return null;
        }
        var folder = Path.GetFullPath(beatmap.LocalPath);
        var prefix = customSongs + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder)
            || beatmap.Id != "local:" + Path.GetRelativePath(customSongs, folder))
        {
            return null;
        }
        CheckParents(folder);
        return folder;
    }

    internal List<BeatmapEntry> Library()
    {
        var result = new List<BeatmapEntry>();
        if (!Directory.Exists(customSongs))
        {
            return result;
        }
        CheckParents(customSongs);
        var managed = new List<string>();
        if (Directory.Exists(songs))
        {
            CheckParents(songs);
            foreach (var folder in Directory.EnumerateDirectories(songs))
            {
                try
                {
                    var id = Path.GetFileName(folder);
                    if (InstalledRevision(id) == null)
                    {
                        continue;
                    }
                    var record = JsonConvert.DeserializeObject<InstalledBeatmap>(File.ReadAllText(Path.Combine(folder, RecordName)))!;
                    var entry = new BeatmapEntry
                    {
                        Id = id, Title = record.Title, Artist = record.Artist, Creator = record.Creator,
                        Revision = new BeatmapRevision { Id = record.RevisionId, Number = record.RevisionNumber },
                        LocalPath = folder, InstalledSize = record.Size,
                    };
                    ReadMetadata(entry, folder);
                    result.Add(entry);
                    managed.Add(folder);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException)
                {
                }
            }
        }
        foreach (var folder in ChartFiles.GetSongFolders(customSongs))
        {
            if (managed.Any(root => folder.Equals(root, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            try
            {
                if (ChartFiles.GetCharts(folder).Count == 0)
                {
                    continue;
                }
                var entry = new BeatmapEntry { Id = "local:" + Path.GetRelativePath(customSongs, folder), LocalPath = folder };
                ReadMetadata(entry, folder);
                result.Add(entry);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
            }
        }
        return result.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadMetadata(BeatmapEntry entry, string folder)
    {
        CheckParents(folder);
        entry.Difficulties = ChartFiles.GetDifficulties(folder);
        if (entry.Title.Length == 0 || entry.Artist.Length == 0 || entry.Creator.Length == 0)
        {
            var chart = new[] { folder }.Concat(ChartFiles.GetSongFolders(folder))
                .SelectMany(ChartFiles.GetCharts).FirstOrDefault();
            if (chart.Path != null && new FileInfo(chart.Path).Length <= 16_777_216)
            {
                var metadata = false;
                foreach (var raw in File.ReadLines(chart.Path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        metadata = line.Equals("[Metadata]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    var split = line.IndexOf(':');
                    if (!metadata || split < 0)
                    {
                        continue;
                    }
                    var key = line.Substring(0, split).Trim();
                    var value = line.Substring(split + 1).Trim();
                    if (key == "Title" && entry.Title.Length == 0) entry.Title = value;
                    if (key == "Artist" && entry.Artist.Length == 0) entry.Artist = value;
                    if (key == "Creator" && entry.Creator.Length == 0) entry.Creator = value;
                }
            }
        }
        if (entry.Title.Length == 0)
        {
            entry.Title = Path.GetFileName(folder);
        }
        if (entry.InstalledSize == 0)
        {
            entry.InstalledSize = new[] { folder }.Concat(ChartFiles.GetSongFolders(folder))
                .SelectMany(Directory.EnumerateFiles).Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .Sum(path => new FileInfo(path).Length);
        }
    }

    internal void Uninstall(BeatmapEntry beatmap, CancellationToken token)
    {
        var folder = InstalledFolder(beatmap) ?? throw new IOException("This beatmap is no longer installed");
        CheckTree(folder);
        token.ThrowIfCancellationRequested();
        CreateDirectory(staging);
        var removed = Path.Combine(staging, Guid.NewGuid().ToString("D"));
        CheckParents(removed);
        Directory.Move(folder, removed);
        try
        {
            Directory.Delete(removed, true);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
        }
    }

    private async Task InstallFiles(BeatNetClient client, BeatmapEntry beatmap, Action<string> progress, CancellationToken token, Action<float>? downloadProgress)
    {
        if (!BeatNetClient.IsId(beatmap.Id) || !BeatNetClient.IsId(beatmap.Revision.Id) || beatmap.Files.Length == 0)
        {
            throw new InvalidDataException("Invalid beatmap details");
        }
        long total = 0;
        foreach (var file in beatmap.Files)
        {
            if (file.Size <= 0 || file.Size > long.MaxValue - total)
            {
                throw new InvalidDataException("The server returned an invalid download size");
            }
            total += file.Size;
        }

        CreateDirectory(staging);
        var workspace = Path.Combine(staging, Guid.NewGuid().ToString("D"));
        var content = Path.Combine(workspace, "content");
        Directory.CreateDirectory(content);
        var committed = false;
        try
        {
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryCount = 0;
            long expanded = 0;
            long received = 0;
            for (var index = 0; index < beatmap.Files.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var file = beatmap.Files[index];
                var archive = Path.Combine(workspace, index + ".zip");
                await client.Download(file, archive,
                    count =>
                    {
                        downloadProgress?.Invoke((received + count) / (float)total);
                        progress($"Downloading {(received + count) * 100 / total}%");
                    }, token).ConfigureAwait(false);
                received += file.Size;
                progress("Checking archive");
                Extract(archive, content, entries, ref entryCount, ref expanded, token);
            }

            progress("Checking beatmaps");
            ValidateCharts(content, token);
            var folder = FindSongRoot(content);
            var record = new InstalledBeatmap
            {
                Id = beatmap.Id, RevisionId = beatmap.Revision.Id, RevisionNumber = beatmap.Revision.Number,
                Title = beatmap.Title, Artist = beatmap.Artist, Creator = beatmap.Creator, Size = total,
            };
            File.WriteAllText(Path.Combine(folder, RecordName), JsonConvert.SerializeObject(record));
            token.ThrowIfCancellationRequested();
            progress("Installing beatmap");
            Commit(folder, beatmap.Id, workspace);
            committed = true;
        }
        finally
        {
            try
            {
                if (committed || !Directory.Exists(Path.Combine(workspace, "previous")))
                {
                    Directory.Delete(workspace, true);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
            }
        }
    }

    private void Extract(string archivePath, string folder, HashSet<string> names, ref int entryCount, ref long expanded, CancellationToken token)
    {
        const long expandedLimit = 2_147_483_648L;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (++entryCount > 10_000)
            {
                throw new InvalidDataException("The archive contains too many entries");
            }
            if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("The archive contains a link");
            }
            var name = entry.FullName.Replace('\\', '/');
            var directory = name.EndsWith("/", StringComparison.Ordinal);
            var path = SafePath(folder, directory ? name.Substring(0, name.Length - 1) : name);
            if (directory)
            {
                Directory.CreateDirectory(path);
                continue;
            }
            if (Path.GetFileName(path).Equals(RecordName, StringComparison.OrdinalIgnoreCase)
                || !names.Add(path) || names.Count > 10_000)
            {
                throw new InvalidDataException("The archive contains duplicate or reserved files");
            }
            if (entry.Length > expandedLimit - expanded)
            {
                throw new InvalidDataException("The extracted beatmap is too large");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536];
            long written = 0;
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                token.ThrowIfCancellationRequested();
                written += count;
                if (written > entry.Length || count > expandedLimit - expanded)
                {
                    throw new InvalidDataException("The extracted beatmap is too large");
                }
                destination.Write(buffer, 0, count);
                expanded += count;
            }
            if (written != entry.Length)
            {
                throw new InvalidDataException("The archive is incomplete");
            }
        }
    }

    private static string SafePath(string root, string name)
    {
        var parts = name.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ", StringComparison.Ordinal)
                || part.EndsWith(".", StringComparison.Ordinal) || part.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*', '\0' }) >= 0
                || part.Any(char.IsControl) || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            {
                throw new InvalidDataException("The beatmap contains an unsafe file path");
            }
        }
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The beatmap contains an unsafe file path");
        }
        return path;
    }

    private static void ValidateCharts(string root, CancellationToken token)
    {
        var count = 0;
        foreach (var folder in new[] { root }.Concat(ChartFiles.GetSongFolders(root)))
        {
            foreach (var chart in ChartFiles.GetCharts(folder))
            {
                token.ThrowIfCancellationRequested();
                if (new FileInfo(chart.Path).Length > 16_777_216)
                {
                    throw new InvalidDataException("A chart file is too large");
                }
                var audio = ReadAudioPath(chart.Path);
                if (audio == null && chart.Slot == null && Path.GetExtension(chart.Path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (audio == null || !File.Exists(SafePath(folder, audio)))
                {
                    throw new InvalidDataException("A chart is missing its audio file");
                }
                count++;
            }
        }
        if (count == 0)
        {
            throw new InvalidDataException("The archive contains no playable beatmaps");
        }
    }

    private static string? ReadAudioPath(string path)
    {
        var general = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                general = line.Equals("[General]", StringComparison.OrdinalIgnoreCase);
            }
            else if (general && line.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring("AudioFilename:".Length).Trim();
            }
        }
        return null;
    }

    private static string FindSongRoot(string folder)
    {
        while (!Directory.EnumerateFiles(folder).Any())
        {
            var directories = Directory.GetDirectories(folder);
            if (directories.Length != 1)
            {
                break;
            }
            folder = directories[0];
        }
        return folder;
    }

    private void Commit(string source, string id, string workspace)
    {
        CreateDirectory(songs);
        var target = Path.Combine(songs, id);
        var backup = Path.Combine(workspace, "previous");
        CheckParents(target);
        var previous = Directory.Exists(target);
        if (previous)
        {
            CheckTree(target);
            Directory.Move(target, backup);
        }
        try
        {
            Directory.Move(source, target);
        }
        catch
        {
            if (previous)
            {
                Directory.Move(backup, target);
            }
            throw;
        }
    }

    private static void CheckTree(string folder)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The install folder contains a link");
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CheckTree(entry);
            }
        }
    }

    private static void CheckParents(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The install folder uses a link");
            }
        }
    }

    private static void CreateDirectory(string path)
    {
        CheckParents(path);
        Directory.CreateDirectory(path);
    }
}
