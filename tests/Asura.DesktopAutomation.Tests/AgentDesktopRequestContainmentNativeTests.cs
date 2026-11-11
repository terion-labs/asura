using Asura.Application;
using static Asura.DesktopAutomation.Tests.AgentDesktopTestRequests;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// What a request may end as, whatever happens inside it: nothing thrown
/// leaves the session, cancellation is read again at the last point before a
/// mutating call, and anything that goes wrong after that call is an unknown
/// outcome, never "nothing delivered".
/// </summary>
/// <remarks>
/// The units of work here make no native call; a real session is used only
/// because a session is built over a loaded library.
/// </remarks>
[Collection(AgentDesktopNativeCollection.Name)]
public sealed class AgentDesktopRequestContainmentNativeTests
{
    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task An_exception_before_the_mutating_call_is_a_closed_failure_with_nothing_delivered()
    {
        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session => new[]
        {
            session.Contain(CancellationToken.None, () => throw new InvalidOperationException("from another application")),
            session.Contain(CancellationToken.None, () => throw new FormatException()),
            session.Contain(CancellationToken.None, () => throw new OverflowException()),
            session.Contain(CancellationToken.None, () => throw new OperationCanceledException()),
        });

        Assert.All(results, result => Assert.Equal(
            new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed),
            Failure(result)));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task An_exception_after_the_mutating_call_began_is_an_unknown_outcome()
    {
        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session => new[]
        {
            session.Contain(CancellationToken.None, () =>
            {
                Assert.Null(session.BeginMutation());
                throw new InvalidOperationException();
            }),
            session.Contain(CancellationToken.None, () =>
            {
                Assert.Null(session.BeginMutation());
                throw new OperationCanceledException();
            }),
        });

        Assert.All(results, result =>
        {
            var failure = Failure(result);
            Assert.Equal(
                new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, AgentDesktopDelivery.Unknown),
                failure);
            Assert.False(failure.Retryable);
        });
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task Cancellation_that_arrives_during_the_checks_stops_the_mutating_call()
    {
        // SessionHost revokes an authorization by cancelling the request; the
        // guard and the listings before the call take time in which that can
        // happen.
        using var revocation = new CancellationTokenSource();
        var (refusal, result) = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            AgentDesktopFailure? refused = null;
            var contained = session.Contain(revocation.Token, () =>
            {
                revocation.Cancel();
                refused = session.BeginMutation();

                // Had the call been considered dispatched, this would read as
                // an unknown outcome.
                throw new InvalidOperationException();
            });
            return (refused, contained);
        });

        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled), refusal);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed), Failure(result));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task A_request_cancelled_before_it_starts_runs_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var ran = false;

        var result = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
            session.Contain(cancelled.Token, () =>
            {
                ran = true;
                return new AgentDesktopExecutionResult.Succeeded(new AgentDesktopResult.AppList([], truncated: false));
            }));

        Assert.False(ran);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled), Failure(result));
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public async Task One_requests_dispatched_mutation_does_not_carry_into_the_next_request()
    {
        using var earlier = new CancellationTokenSource();
        var results = await AgentDesktopTestRuntime.OnSessionAsync(AgentDesktopFlavor.Release, session =>
        {
            var dispatched = session.Contain(earlier.Token, () =>
            {
                Assert.Null(session.BeginMutation());
                throw new InvalidOperationException();
            });

            // The earlier request's token is not the next one's either.
            earlier.Cancel();
            var next = session.Contain(CancellationToken.None, () =>
            {
                Assert.Null(session.BeginMutation());
                return new AgentDesktopExecutionResult.Succeeded(new AgentDesktopResult.AppList([], truncated: false));
            });
            var after = session.Contain(CancellationToken.None, () => throw new InvalidOperationException());
            return new[] { dispatched, next, after };
        });

        Assert.Equal(AgentDesktopFailureCode.OutcomeUnknown, Failure(results[0]).Code);
        Assert.IsType<AgentDesktopExecutionResult.Succeeded>(results[1]);
        Assert.Equal(new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed), Failure(results[2]));
    }
}
