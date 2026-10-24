using System.Collections.Concurrent;
using Exclr8Cef;

namespace Asura.Browser;

/// <summary>
/// Limits decoded network bodies for one detached web call before Chromium
/// delivers them to its renderer. Ordinary user browser panels are unaffected.
/// </summary>
internal sealed class AgentWebResponseBudget : IDisposable
{
    internal const long MaximumDecodedBytes = 16 * 1024 * 1024;
    internal const int MaximumResponses = 256;
    private static readonly ConcurrentDictionary<int, AgentWebResponseBudget> Browsers = new();
    private long _decodedBytes;
    private int _responses;
    private int _exceeded;
    private int _browserId;

    static AgentWebResponseBudget()
    {
        // Asura owns these process-wide callbacks. CEF invokes them synchronously
        // on its IO thread; no UI work, decoding or content retention belongs here.
        Cef.ShouldFilterResponse = decision => decision.Browser is { } browser
            && Browsers.TryGetValue(browser.Id, out var budget)
            && budget.BeginResponse();
        Cef.ResponseFilter = Filter;
    }

    public bool Exceeded => Volatile.Read(ref _exceeded) != 0;

    public void Register(int browserId)
    {
        if (_browserId != 0 && _browserId != browserId)
        {
            Browsers.TryRemove(_browserId, out _);
        }
        _browserId = browserId;
        Browsers[browserId] = this;
    }

    internal bool BeginResponse()
    {
        if (Interlocked.Increment(ref _responses) > MaximumResponses)
        {
            Interlocked.Exchange(ref _exceeded, 1);
        }
        // Install the filter even after exhaustion so each later response fails.
        return true;
    }

    internal Cef.ResponseFilterStatus Copy(
        ReadOnlySpan<byte> input, Span<byte> output, out int bytesRead, out int bytesWritten)
    {
        bytesRead = 0;
        bytesWritten = 0;
        if (Exceeded) { return Cef.ResponseFilterStatus.Error; }
        var count = Math.Min(input.Length, output.Length);
        if (Interlocked.Add(ref _decodedBytes, count) > MaximumDecodedBytes)
        {
            Interlocked.Exchange(ref _exceeded, 1);
            return Cef.ResponseFilterStatus.Error;
        }
        input[..count].CopyTo(output);
        bytesRead = count;
        bytesWritten = count;
        return input.IsEmpty ? Cef.ResponseFilterStatus.Done : Cef.ResponseFilterStatus.NeedMoreData;
    }

    public void Dispose()
    {
        if (_browserId != 0) { Browsers.TryRemove(_browserId, out _); }
    }

    private static Cef.ResponseFilterStatus Filter(
        int browserId, ulong token, ReadOnlySpan<byte> input, Span<byte> output,
        out int bytesRead, out int bytesWritten)
    {
        _ = token;
        if (Browsers.TryGetValue(browserId, out var budget))
        {
            return budget.Copy(input, output, out bytesRead, out bytesWritten);
        }
        // An installed filter outliving its detached browser must stop, not
        // silently turn into an unmetered response after the registry is removed.
        bytesRead = 0;
        bytesWritten = 0;
        return Cef.ResponseFilterStatus.Error;
    }
}
