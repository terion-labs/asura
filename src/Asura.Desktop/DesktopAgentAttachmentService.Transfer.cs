using System.Diagnostics;
using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

internal sealed partial class DesktopAgentAttachmentService
{
    private static async Task<string> RunAttachmentCommandAsync(IConnectionCommandRuntime runtime, string script,
        IReadOnlyList<string> arguments, byte[] bytes, CancellationToken token)
    {
        var planned = await runtime.PlanDuplexCommandAsync(BuiltInConnections.Local, "/bin/sh",
            ["-c", script, "asura-attachment", .. arguments], token).ConfigureAwait(false);
        if (planned is not ConnectionRuntimeResult<TerminalLaunchRequest>.Success { Value: var launch })
        {
            throw new IOException("The attachment could not be transferred into this workspace. Reopen the workspace and retry.");
        }
        var start = new ProcessStartInfo(launch.Executable ?? throw new InvalidOperationException("No workspace executable."))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in launch.Arguments) { start.ArgumentList.Add(argument); }
        foreach (var (name, value) in launch.Environment) { start.Environment[name] = value; }
        if (launch.WorkingDirectory is { } directory) { start.WorkingDirectory = directory; }
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            if (!await Task.Run(process.Start, timeout.Token).ConfigureAwait(false)) { throw new IOException("Attachment transfer did not start."); }
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new IOException("The workspace could not finish the attachment transfer. Retry opening the attachment.");
            }
            return await output.ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new IOException("The workspace could not start the attachment transfer. Reopen the workspace and retry.", exception);
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { /* The transfer may not have started. */ }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var count = await reader.ReadBlockAsync(buffer, token).ConfigureAwait(false);
        if (count == buffer.Length) { throw new IOException("Attachment transfer returned too much output."); }
        return new string(buffer, 0, count);
    }
}
