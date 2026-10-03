using System.Globalization;
using System.Text;
using Asura.Application;

namespace Asura.Browser;

/// <summary>Bounded process metadata only; never accepts URLs or native error text.</summary>
internal sealed class BrowserRendererExitLog(string path)
{
    internal const int MaximumBytes = 65_536;
    private readonly object _gate = new();

    public void Record(int browserId, int status, int exitCode, bool navigating)
    {
        lock (_gate)
        {
            try
            {
                if (new FileInfo(path).LinkTarget is not null)
                {
                    return;
                }

                var options = new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                using var stream = new FileStream(path, options);
                var line = string.Create(CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.UtcNow:O} process={Environment.ProcessId} browser={browserId} status={status} exit={exitCode} navigating={(navigating ? 1 : 0)}\n");
                var bytes = Encoding.UTF8.GetBytes(line);
                if (stream.Length + bytes.Length > MaximumBytes)
                {
                    stream.SetLength(0);
                }

                stream.Seek(0, SeekOrigin.End);
                stream.Write(bytes);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Failure to persist diagnostics must never interrupt renderer recovery.
                SecretSafeDiagnosticProjection.WriteStandardError("browser.renderer-exit-log.failed", error);
            }
        }
    }
}
