using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Update;

namespace OpenScct.Launcher.Editor;

public sealed partial class EditorService
{
    /// <summary>
    /// Removes this launcher's editor installation. The complete folder is first moved to a unique sibling, then
    /// only unchanged, manifest-listed package files are deleted. User projects, modified files and unknown folders
    /// stay in that sibling. No recursive delete is used, and game/shared files and preferences are never visited.
    /// Cancellation is honoured until the move; afterwards cleanup finishes and returns any retained location.
    /// </summary>
    public async Task<EditorUninstallResult> UninstallAsync(IProgress<EditorProgress>? progress, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Editor uninstall is supported on Windows only.");
        cancel.ThrowIfCancellationRequested();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layout.Root));
        string editor = Child(root, "editor");
        RefuseLinkedAncestors(editor);
        if (!Directory.Exists(editor)) throw new InvalidOperationException("The editor is not installed.");
        RefuseLinkedTree(editor);
        string marker = Path.Combine(editor, ManagedMarker);
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != "1")
            throw new InvalidOperationException("This editor folder is not managed by the launcher. No files were removed.");
        if (NeedsRecovery)
            throw new InvalidOperationException("An editor update needs recovery. Finish that recovery before uninstalling the editor.");
        if (IsRunning)
            throw new InvalidOperationException("Save your work and close the editor, its command-line tools and its updater before uninstalling.");

        progress?.Report(new("Checking editor files"));
        EditorManifest manifest;
        try { manifest = EditorManifest.Read(editor); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidDataException("The editor package manifest is missing or invalid. No files were removed.", ex);
        }
        ValidateUninstallManifest(manifest);
        var files = manifest.Files.ToList();
        files.Add(await SnapshotFileAsync(editor, "version.json", cancel).ConfigureAwait(false));
        files.Add(await SnapshotFileAsync(editor, ManagedMarker, cancel).ConfigureAwait(false));
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".update" };
        foreach (EditorFile file in files)
        {
            string? parent = Path.GetDirectoryName(file.Path.Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(parent))
            {
                directories.Add(parent);
                parent = Path.GetDirectoryName(parent);
            }
        }

        RestoreUninstallBarrier(root, editor);
        // The updater also uses this lock from helpers outside the editor directory. Windows cannot rename a
        // directory with open files inside it: relocate the held lock, then put a marked directory at its old path.
        // The updater's OpenOrCreate fails closed against that directory until the editor folder is moved.
        string update = Child(editor, ".update");
        Directory.CreateDirectory(update);
        string lockPath = Child(update, "swap.lock");
        FileStream held;
        try { held = OpenUninstallFile(lockPath, FileAccess.ReadWrite, FileShare.Delete, create: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The editor updater is busy. Wait for it to finish before uninstalling.", ex);
        }

        string? retained = null;
        var failures = new List<string>();
        string token = Guid.NewGuid().ToString("N");
        string holding = Child(root, ".editor-uninstall-lock-" + token);
        string barrier = Child(update, ".uninstall-barrier-" + token);
        using (held)
        {
            // A staging update may have finished while the manifest was read. Check again with the swap lock held.
            RefuseLinkedTree(editor);
            if (NeedsRecovery || (RunningOverride?.Invoke(editor) ?? RunningIn(editor)))
                throw new InvalidOperationException("The editor or its updater is now active. Close it and finish any update recovery before uninstalling.");
            foreach (EditorFile metadata in files.TakeLast(2))
            {
                if (!await MatchesAsync(Child(editor, metadata.Path), metadata, cancel).ConfigureAwait(false))
                    throw new InvalidOperationException("The editor installation changed while it was being checked. Try uninstalling again.");
            }
            cancel.ThrowIfCancellationRequested();
            retained = Child(root, "editor-preserved-" + Guid.NewGuid().ToString("N"));
            progress?.Report(new("Uninstalling editor", CanCancel: false));
            Directory.CreateDirectory(barrier);
            File.WriteAllText(Path.Combine(barrier, BarrierMarker), token);
            try
            {
                File.Move(lockPath, holding);
                // If an updater wins this gap, its lock file prevents this move. No editor files were removed.
                Directory.Move(barrier, lockPath);
                if (NeedsRecovery || (RunningOverride?.Invoke(editor) ?? RunningIn(editor)))
                    throw new InvalidOperationException("The editor or updater started while uninstalling. Close it and try again.");
                Directory.Move(editor, retained);
            }
            catch
            {
                held.Dispose();
                RestoreUninstallBarrier(root, editor);
                throw;
            }
            progress?.Report(new("Removing unchanged editor files", CanCancel: false));

            foreach (EditorFile file in files)
            {
                try { await DeleteUnchangedAsync(retained, file).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failures.Add(file.Path + ": " + ex.Message);
                }
            }

            try
            {
                DeleteBarrier(Child(retained, ".update/swap.lock"), token);
                // Retain unfamiliar lock contents rather than discard them.
                if (held.Length == 0) MarkForDeletion(held.SafeFileHandle);
                else File.Move(holding, Child(retained, ".update/swap.lock"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add("Updater lock cleanup: " + ex.Message + " Retained lock: " + holding);
            }
        }

        foreach (string relative in directories.OrderByDescending(d => d.Length))
        {
            try { DeleteEmptyDirectory(Child(retained!, relative.Replace('\\', '/'))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add(relative + ": " + ex.Message);
            }
        }
        try { DeleteEmptyDirectory(retained!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { failures.Add(ex.Message); }
        progress?.Report(new("Editor uninstalled", CanCancel: false));
        return new EditorUninstallResult(Directory.Exists(retained) ? retained : null, failures);
    }

    private static void ValidateUninstallManifest(EditorManifest manifest)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "version.json", ManagedMarker };
        foreach (EditorFile file in manifest.Files)
        {
            if (file is null || !UnambiguousRelative(file.Path) || file.Size < 0 || string.IsNullOrEmpty(file.Sha256)
                || !Checksums.IsSha256Hex(file.Sha256) || !listed.Add(file.Path)
                || !manifest.Entries.Contains(file.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("The editor file manifest is invalid. No files were removed.");
        }
        if (!listed.Contains(ExeName) || !listed.Contains("ose/ose.exe")
            || !listed.Contains("data_OpenScctEditor_windows_x86_64/OpenScctEditor.dll")
            || manifest.Entries.Any(e => !UnambiguousRelative(e))
            || listed.Any(p => p.Contains('/') && listed.Contains(p[..p.LastIndexOf('/')])))
            throw new InvalidDataException("The editor file manifest is incomplete or ambiguous. No files were removed.");
        // A manifest cannot name a file and also a descendant of that file.
        foreach (string file in listed)
        {
            for (int slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1))
            {
                if (listed.Contains(file[..slash])) throw new InvalidDataException("The editor file manifest contains overlapping paths. No files were removed.");
            }
        }
    }

    private static bool UnambiguousRelative(string path) => EditorManifest.SafeRelative(path) && path.Split('/').All(part =>
    {
        string stem = part.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789¹²³".Contains(stem[3], StringComparison.Ordinal));
    });

    private static async Task<EditorFile> SnapshotFileAsync(string root, string relative, CancellationToken cancel)
    {
        string path = Child(root, relative);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        long length = input.Length;
        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancel).ConfigureAwait(false));
        return new EditorFile { Path = relative, Size = length, Sha256 = hash };
    }

    private static async Task<bool> MatchesAsync(string path, EditorFile file, CancellationToken cancel)
    {
        if (!File.Exists(path)) return false;
        return new FileInfo(path).Length == file.Size && await Checksums.VerifyAsync(path, file.Sha256, cancel).ConfigureAwait(false);
    }

    private static async Task DeleteUnchangedAsync(string root, EditorFile file)
    {
        string path = Child(root, file.Path);
        RefuseLinkedAncestors(path);
        if (!File.Exists(path)) return;
        // Deny writes and replacement while checking; deletion targets this verified handle, never a later path occupant.
        await using FileStream input = OpenUninstallFile(path, FileAccess.Read, FileShare.Read);
        if (input.Length != file.Size || !string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(input).ConfigureAwait(false)), file.Sha256, StringComparison.OrdinalIgnoreCase)) return;
        MarkForDeletion(input.SafeFileHandle);
    }

    private const string BarrierMarker = ".launcher-editor-uninstall";

    /// <summary>Restores only our marked lock barrier after an interrupted preflight/move. User files stay put.</summary>
    private static void RestoreUninstallBarrier(string root, string editor)
    {
        string update = Child(editor, ".update");
        if (!Directory.Exists(update)) return;
        string lockPath = Child(update, "swap.lock");
        List<string> candidates = Directory.GetDirectories(update, ".uninstall-barrier-*").ToList();
        if (Directory.Exists(lockPath)) candidates.Add(lockPath);
        foreach (string candidate in candidates)
        {
            string? token = BarrierToken(candidate);
            if (token is null)
            {
                if (candidate == lockPath) throw new InvalidOperationException("The editor updater lock path contains an unrecognized directory. Its files were preserved.");
                continue;
            }
            string holding = Child(root, ".editor-uninstall-lock-" + token);
            FileStream? held = null;
            try
            {
                if (File.Exists(holding))
                {
                    RefuseLinkedAncestors(holding);
                    try { held = OpenUninstallFile(holding, FileAccess.ReadWrite, FileShare.Delete); }
                    catch (IOException ex) { throw new InvalidOperationException("An editor uninstall is already in progress. Wait for it to finish.", ex); }
                }
                // Move the complete marker away atomically. A crash here leaves a recognizable prepared barrier.
                string prepared = candidate;
                if (candidate == lockPath)
                {
                    prepared = Child(update, ".uninstall-barrier-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(candidate, prepared);
                }
                if (held is not null)
                {
                    if (File.Exists(lockPath))
                    {
                        if (held.Length != 0) throw new InvalidOperationException("An updater acquired the editor while uninstall recovery was pending. Close it and try again.");
                        MarkForDeletion(held.SafeFileHandle);
                    }
                    else File.Move(holding, lockPath);
                }
                DeleteBarrier(prepared, token);
            }
            finally { held?.Dispose(); }
        }
    }

    private static string? BarrierToken(string directory)
    {
        RefuseLinkedAncestors(directory);
        string[] entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length != 1 || Path.GetFileName(entries[0]) != BarrierMarker || !File.Exists(entries[0])) return null;
        RefuseLink(entries[0]);
        string token = File.ReadAllText(entries[0]);
        return Guid.TryParseExact(token, "N", out _) ? token : null;
    }

    private static void DeleteBarrier(string directory, string token)
    {
        if (BarrierToken(directory) != token) throw new InvalidOperationException("The uninstall lock marker changed; its files were preserved.");
        using (FileStream marker = OpenUninstallFile(Path.Combine(directory, BarrierMarker), FileAccess.Read, FileShare.Read))
        {
            using var text = new StreamReader(marker, leaveOpen: true);
            if (text.ReadToEnd() != token) throw new InvalidOperationException("The uninstall lock marker changed; its files were preserved.");
            MarkForDeletion(marker.SafeFileHandle);
        }
        Directory.Delete(directory, recursive: false);
    }

    private static string Child(string root, string relative)
    {
        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(boundary, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || path.Length <= boundary.Length)
            throw new InvalidOperationException("The editor path is outside its installation folder.");
        return path;
    }

    private static void RefuseLinkedAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)))
        {
            if (File.Exists(current) || Directory.Exists(current)) RefuseLink(current);
            if (Path.GetPathRoot(current) == current) break;
        }
    }

    private static void RefuseLinkedTree(string folder)
    {
        RefuseLink(folder);
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.TryPop(out string? directory))
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                RefuseLink(entry.FullName);
                if ((entry.Attributes & FileAttributes.Directory) != 0) pending.Push(entry.FullName);
            }
        }
    }

    private static void RefuseLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The editor folder contains a symbolic link or junction. No files through that link will be removed: " + path);
    }

    private static void DeleteEmptyDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        RefuseLinkedAncestors(path);
        if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, recursive: false);
    }
}
