using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

internal sealed class KubernetesExecSession : IKubernetesExecSession
{
    private readonly KubernetesChannelConnection _connection;
    private readonly bool _tty;
    private readonly Task<int> _exit;

    public KubernetesExecSession(KubernetesChannelConnection connection, bool tty)
    {
        _connection = connection;
        _tty = tty;
        _exit = ReadExitAsync();
    }

    public Stream StandardInput => _connection.GetStream(0);

    public Stream StandardOutput => _connection.GetStream(1);

    public Stream StandardError => _connection.GetStream(2);

    public ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) => new(_exit.WaitAsync(cancellationToken));

    public ValueTask CompleteInputAsync(CancellationToken cancellationToken) => _connection.SendAsync(255, new byte[] { 0 }, cancellationToken);

    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        if (!_tty)
        {
            return ValueTask.CompletedTask;
        }

        if (columns is < 1 or > 1000 || rows is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Terminal dimensions must be between 1 and 1000 cells.");
        }

        byte[] json = Encoding.UTF8.GetBytes(new JsonObject { ["Width"] = columns, ["Height"] = rows }.ToJsonString());
        return _connection.SendAsync(4, json, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _exit.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is KubernetesRequestException or OperationCanceledException or System.Threading.Channels.ChannelClosedException)
        {
            // A disposed terminal has no reliable remote exit status; callers observe it through WaitForExitAsync.
        }
    }

    private async Task<int> ReadExitAsync()
    {
        Stream stream = _connection.GetStream(3);
        using var content = new MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (content.Length + read > 65536)
            {
                throw Failure();
            }

            content.Write(buffer, 0, read);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("status", out JsonElement status) && status.ValueEquals("Success"))
            {
                return 0;
            }

            if (root.TryGetProperty("details", out JsonElement details) && details.TryGetProperty("causes", out JsonElement causes)
                && causes.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement cause in causes.EnumerateArray())
                {
                    if (cause.TryGetProperty("reason", out JsonElement reason) && reason.ValueEquals("ExitCode")
                        && cause.TryGetProperty("message", out JsonElement message)
                        && int.TryParse(message.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out int exitCode))
                    {
                        return exitCode;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Failure();
        }

        throw Failure();
    }

    private static KubernetesRequestException Failure() =>
        new(KubernetesErrorCode.ConnectionFailed, "The pod terminal ended without a valid exit status.");
}
