namespace Asura.App.Views;

/// <summary>
/// Runs asynchronous operations one after another: each starts once the one
/// before it has finished, however that one finished. Used from the UI thread
/// only.
/// </summary>
internal sealed class InTurn
{
    private Task _last = Task.CompletedTask;

    public Task<T> Run<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var run = AfterAsync(_last, operation);
        _last = run;
        return run;
    }

    private static async Task<T> AfterAsync<T>(Task previous, Func<Task<T>> operation)
    {
        // The one before reports its own failure to whoever started it.
        await previous.ConfigureAwait(
            ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        return await operation();
    }
}
