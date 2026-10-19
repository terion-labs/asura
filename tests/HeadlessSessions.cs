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
        if (WorkerTask(session) is null)
        {
            RestoreWorkerTask(session);
        }

        return session;
    }

    internal static Task? WorkerTask(HeadlessUnitTestSession session) => (Task?)DispatchTask.GetValue(session);

    internal static void ForgetWorkerTask(HeadlessUnitTestSession session) => DispatchTask.SetValue(session, null);

    internal static void RestoreWorkerTask(HeadlessUnitTestSession session)
    {
        // A dispatched body runs synchronously on the worker, inside its task.
        var worker = session
            .Dispatch(() => new StrongBox<Task?>((Task?)CurrentTask.GetValue(null)), CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Value;
        DispatchTask.SetValue(
            session,
            worker ?? throw new InvalidOperationException("The headless session worker has no task."));
    }
}
