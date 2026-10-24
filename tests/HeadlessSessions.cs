using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Headless;

namespace Asura.HeadlessDispatch.Tests;

/// <summary>
/// Avalonia's <c>HeadlessUnitTestSession.StartNew</c> gives the session its
/// worker task through a closure the worker reads before <c>Task.Run</c> has
/// returned and assigned it. When the worker wins that race the session keeps
/// a null task, and disposing it fails with a NullReferenceException after the
/// test itself passed. Every headless test starts its session here, which
/// hands a session that lost the race its worker task back.
///
/// Disposal races too: it cancels the worker, then completes its queue. A
/// worker that reaches its next <c>Take</c> between the two finds the queue
/// complete before it sees the cancellation, and <c>Take</c> throws
/// InvalidOperationException, which faulted the worker and failed disposal.
/// The session is given a worker task that ends normally in that case.
/// </summary>
public static class HeadlessSessions
{
    private const DynamicallyAccessedMemberTypes EntryPointMembers =
        DynamicallyAccessedMemberTypes.PublicMethods
        | DynamicallyAccessedMemberTypes.NonPublicMethods
        | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    private static readonly FieldInfo DispatchTask =
        typeof(HeadlessUnitTestSession).GetField("_dispatchTask", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(HeadlessUnitTestSession), "_dispatchTask");

    private static readonly PropertyInfo CurrentTask =
        typeof(Task).GetProperty("InternalCurrent", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(Task), "InternalCurrent");

    public static HeadlessUnitTestSession StartNew([DynamicallyAccessedMembers(EntryPointMembers)] Type entryPointType)
    {
        var session = HeadlessUnitTestSession.StartNew(entryPointType);
        DispatchTask.SetValue(session, ToleratingCompletedQueue(WorkerTask(session) ?? ReadWorkerTask(session)));
        return session;
    }

    /// <summary>
    /// The worker's own task, read on the worker. Work dispatched to it runs
    /// synchronously there, inside that task.
    /// </summary>
    internal static Task ReadWorkerTask(HeadlessUnitTestSession session) =>
        session
            .Dispatch(() => new StrongBox<Task?>((Task?)CurrentTask.GetValue(null)), CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Value
        ?? throw new InvalidOperationException("The headless session worker has no task.");

    /// <summary>
    /// Ends when the worker does, and normally when only the completed queue
    /// stopped it: dispatched work reports its own failures to its caller, so
    /// the worker loop's only InvalidOperationException is that <c>Take</c>.
    /// </summary>
    internal static Task ToleratingCompletedQueue(Task worker) =>
        worker.ContinueWith(
            static task =>
            {
                if (task.Exception?.InnerExceptions is [InvalidOperationException])
                {
                    return;
                }

                task.GetAwaiter().GetResult();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    internal static Task? WorkerTask(HeadlessUnitTestSession session) => (Task?)DispatchTask.GetValue(session);

    internal static void ForgetWorkerTask(HeadlessUnitTestSession session) => DispatchTask.SetValue(session, null);

    internal static void RestoreWorkerTask(HeadlessUnitTestSession session) =>
        DispatchTask.SetValue(session, ToleratingCompletedQueue(ReadWorkerTask(session)));
}
