using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Asura.App;

/// <summary>
/// An item the Trash took, and where it went: enough to put it back.
/// </summary>
/// <param name="OriginalPath">Where the item was.</param>
/// <param name="TrashedPath">
/// Where it lives in the Trash now, or null when the system did not say — an
/// item whose new home is unknown can still be found in the Trash by hand,
/// but cannot be put back from here.
/// </param>
public sealed record TrashedItem(string OriginalPath, string? TrashedPath);

/// <summary>
/// The system Trash, for files on the machine Asura runs on.
///
/// Deleting a local file used to be permanent, behind a confirmation that said
/// so. On a Mac the expected gesture is Command-Delete, which moves the item to
/// the Trash at once, asks nothing, and can be undone. A provider that is not
/// this machine has no Trash of its own and keeps the confirmed permanent
/// delete.
/// </summary>
public interface IFileTrash
{
    /// <summary>What the system calls it: "Trash" on macOS and Linux desktops.</summary>
    string Name { get; }

    /// <summary>
    /// Moves an item to the Trash. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the system refuses; the
    /// item is then where it was.
    /// </summary>
    TrashedItem MoveToTrash(string path);

    /// <summary>
    /// Puts a trashed item back where it was. Throws <see cref="IOException"/>
    /// when something new already occupies that place, or the item has left
    /// the Trash.
    /// </summary>
    void PutBack(TrashedItem item);
}

/// <summary>The Trash this machine has, if Asura knows how to reach it.</summary>
public static class FileTrash
{
    /// <summary>
    /// The system Trash, or null where there is none Asura can use — Windows'
    /// Recycle Bin needs the shell's own file operation and is not wired yet,
    /// so local deletes there stay confirmed and permanent.
    /// </summary>
    public static IFileTrash? ForThisMachine { get; } = Create();

    private static IFileTrash? Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new Views.MacOsFileTrash();
        }

        return OperatingSystem.IsLinux()
            ? FreedesktopFileTrash.ForCurrentUser()
            : null;
    }

    /// <summary>
    /// Moves an item back from wherever the Trash put it. Shared by every
    /// Trash: putting back is a rename, whichever system did the taking.
    /// </summary>
    internal static void MoveBack(TrashedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.TrashedPath is not { } trashed)
        {
            throw new IOException(
                $"The Trash didn't say where “{Path.GetFileName(item.OriginalPath)}” went.");
        }

        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
        {
            throw new IOException(
                $"An item named “{Path.GetFileName(item.OriginalPath)}” is already in that folder.");
        }

        if (Directory.Exists(trashed))
        {
            Directory.Move(trashed, item.OriginalPath);
        }
        else if (File.Exists(trashed))
        {
            File.Move(trashed, item.OriginalPath);
        }
        else
        {
            throw new IOException(
                $"“{Path.GetFileName(item.OriginalPath)}” is no longer in the Trash.");
        }
    }
}

/// <summary>
/// The Trash as the freedesktop.org specification lays it out, which GNOME,
/// KDE and their file managers share: the item moves into
/// <c>Trash/files</c> and a <c>.trashinfo</c> beside it in <c>Trash/info</c>
/// records where it came from and when, so the desktop's own Restore works.
///
/// Only the home Trash is used. An item on another file system cannot be
/// renamed into it, and is refused rather than copied: copying a large tree
/// across devices is a transfer, not a delete, and could fill the home
/// volume. The move is therefore a bare <c>rename(2)</c>; <see cref="File.Move(string, string)"/>
/// would quietly fall back to copy-and-delete across devices.
/// </summary>
internal sealed class FreedesktopFileTrash : IFileTrash
{
    // The same numbers on Linux and macOS.
    private const int ErrorPermission = 1;
    private const int ErrorAccess = 13;
    private const int ErrorCrossDevice = 18;

    private readonly string _files;
    private readonly string _info;
    private readonly Func<DateTime> _now;

    public FreedesktopFileTrash(string trashDirectory, Func<DateTime>? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trashDirectory);
        _files = Path.Combine(trashDirectory, "files");
        _info = Path.Combine(trashDirectory, "info");
        _now = now ?? (() => DateTime.Now);
    }

    public string Name => "Trash";

    public static FreedesktopFileTrash? ForCurrentUser()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathRooted(dataHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                return null;
            }

            dataHome = Path.Combine(home, ".local", "share");
        }

        return new FreedesktopFileTrash(Path.Combine(dataHome, "Trash"));
    }

    public TrashedItem MoveToTrash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
        {
            throw new IOException($"“{Path.GetFileName(source)}” no longer exists.");
        }

        Directory.CreateDirectory(_files);
        Directory.CreateDirectory(_info);
        var (name, infoPath) = ReserveName(Path.GetFileName(source), source);
        var destination = Path.Combine(_files, name);
        try
        {
            Rename(source, destination);
        }
        catch
        {
            File.Delete(infoPath);
            throw;
        }

        return new TrashedItem(source, destination);
    }

    /// <summary>
    /// Renames within one file system and nothing more. An item on another
    /// device fails with <c>EXDEV</c>, which becomes an <see cref="IOException"/>
    /// saying so; the item stays where it was.
    /// </summary>
    internal static void Rename(string source, string destination)
    {
        if (rename(NativePath(source), NativePath(destination)) == 0)
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        var name = Path.GetFileName(source);
        // The error number goes to diagnostics, not the message people read.
        Asura.Application.SecretSafeDiagnosticProjection.WriteTrace(
            string.Create(CultureInfo.InvariantCulture, $"files.trash.rename-failed.errno-{Math.Abs(error)}"),
            Asura.Application.SecretSafeDiagnosticKind.Io);
        throw error switch
        {
            ErrorCrossDevice => new IOException(
                $"“{name}” is on a different disk than the Trash, so it wasn't moved. Delete it instead."),
            ErrorPermission or ErrorAccess => new UnauthorizedAccessException(
                $"“{name}” couldn't be moved to the Trash. Check its permissions, then try again."),
            _ => new IOException(
                $"“{name}” couldn't be moved to the Trash. Try again.",
                error),
        };
    }

    public void PutBack(TrashedItem item)
    {
        FileTrash.MoveBack(item);
        if (item.TrashedPath is { } trashed)
        {
            File.Delete(Path.Combine(_info, Path.GetFileName(trashed) + ".trashinfo"));
        }
    }

    /// <summary>
    /// Claims a name in the Trash by creating its info file exclusively, as the
    /// specification asks, so two deletes of "notes.txt" never overwrite each
    /// other: the second becomes "notes.2.txt".
    /// </summary>
    private (string Name, string InfoPath) ReserveName(string fileName, string source)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var attempt = 1; attempt < 10_000; attempt++)
        {
            var name = attempt == 1
                ? fileName
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stem}.{attempt}{extension}");
            var infoPath = Path.Combine(_info, name + ".trashinfo");
            if (File.Exists(Path.Combine(_files, name)) || Directory.Exists(Path.Combine(_files, name)))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
                var info = string.Create(
                    CultureInfo.InvariantCulture,
                    $"[Trash Info]\nPath={EscapePath(source)}\nDeletionDate={_now():yyyy-MM-ddTHH:mm:ss}\n");
                stream.Write(Encoding.UTF8.GetBytes(info));
                return (name, infoPath);
            }
            catch (IOException) when (File.Exists(infoPath))
            {
                // Another delete took this name between the check and the
                // create; try the next one.
            }
        }

        throw new IOException($"The Trash has no free name for “{fileName}”.");
    }

    /// <summary>The path as the info file records it: each segment URL-escaped.</summary>
    internal static string EscapePath(string path) =>
        string.Join(
            '/',
            path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>A path as the C library takes it: UTF-8, ending in a zero byte.</summary>
    private static byte[] NativePath(string path) => Encoding.UTF8.GetBytes(path + "\0");

    [DllImport("libc", SetLastError = true)]
    private static extern int rename(byte[] oldPath, byte[] newPath);
}
