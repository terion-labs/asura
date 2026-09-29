using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    private bool _externalRun;

    /// <summary>The trusted desktop attachment supplies scope, never a remote caller.</summary>
    public AgentTarget.Workspace? ExternalToolTarget
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _workspaceLayoutPort is { } port
                    ? new AgentTarget.Workspace(port.WindowId, port.WorkspaceId)
                    : null;
            }
        }
    }

    public async ValueTask<ImmutableArray<AgentToolDefinition>> ListExternalToolsAsync(
        CancellationToken cancellationToken)
    {
        var target = ExternalToolTarget;
        if (target is null)
        {
            return [];
        }

        var context = await InspectRunTargetContextAsync(target, _approvalActor, cancellationToken)
            .ConfigureAwait(false);
        if (context is null)
        {
            return [];
        }

        return await BuildExternalToolsAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ImmutableArray<AgentToolDefinition>> BuildExternalToolsAsync(
        AgentContextSnapshot context, CancellationToken cancellationToken)
    {
        var resize = await InspectResizeAttachmentsAsync(context, cancellationToken).ConfigureAwait(false);
        var browser = await InspectBrowserAttachmentsAsync(context, cancellationToken).ConfigureAwait(false);
        var files = await InspectFileSessionsAsync(context, cancellationToken).ConfigureAwait(false);
        // Only native catalog entries cross this boundary. Rebuild the sequence enum
        // from those entries so a restored downstream MCP manifest cannot leak into it.
        ImmutableArray<AgentToolDefinition> native = [.. BuildAgentTools(
                context, resize.Keys.ToImmutableHashSet(), browser, files)
            .Where(tool => _toolCatalog.TryGet(tool.Name, out _))];
        return [.. native, AgentSequenceIntrinsic.Build(native)];
    }

    /// <summary>
    /// Executes a remote proposal through the same live-scope checks, approvals and audit as
    /// the internal agent. A workspace has one operator at a time; Clear releases its run.
    /// </summary>
    public async ValueTask<AgentToolResult> CallExternalToolAsync(
        string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var proposal = new AgentToolProposal(id, 1, id, toolName, arguments);
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed || _clearing || _policyChangeInFlight || _turnCancellation is not null)
            {
                return CreateRejectedResult(proposal, "agent_busy");
            }

            if (_restoredSession is not null
                || (_session is not null && !_externalRun)
                || _snapshot.State is GovernedAgentState.Failed or GovernedAgentState.Cancelled)
            {
                return CreateRejectedResult(proposal, "agent_run_requires_clear");
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _turnCancellation = cancellation;
        }

        try
        {
            var target = ExternalToolTarget;
            if (target is null)
            {
                return CreateRejectedResult(proposal, "target_changed");
            }

            var context = await InspectRunTargetContextAsync(target, GetOrCreateAgent(), cancellation.Token)
                .ConfigureAwait(false);
            if (context is null)
            {
                return CreateRejectedResult(proposal, "target_changed");
            }

            var tools = await BuildExternalToolsAsync(context, cancellation.Token).ConfigureAwait(false);
            if (!tools.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal)))
            {
                return CreateRejectedResult(proposal, "unknown_tool");
            }

            var policy = await _workspaceLayoutPort!.ResolveExternalPolicyAsync(target, [], cancellation.Token)
                .ConfigureAwait(false);
            if (policy is null) { return CreateRejectedResult(proposal, "agent_policy_unavailable"); }
            policy = AgentPolicyResolver.Resolve(policy);
            var request = new GovernedAgentPrompt(new AiProviderProfileId(policy.Provider),
                "External MCP operator", target, policy);
            var resize = await InspectResizeAttachmentsAsync(context, cancellation.Token).ConfigureAwait(false);
            var browser = await InspectBrowserAttachmentsAsync(context, cancellation.Token).ConfigureAwait(false);
            var files = await InspectFileSessionsAsync(context, cancellation.Token).ConfigureAwait(false);
            if (!TryPinOrValidateRun(request, policy, context,
                    resize.Keys.ToImmutableHashSet(), browser, files, out var error))
            {
                return CreateRejectedResult(proposal, error!.Code);
            }

            lock (_gate)
            {
                _externalRun = true;
            }

            if (!_runRegistered)
            {
                var registrationError = await RegisterRunAsync(request, cancellation.Token).ConfigureAwait(false);
                if (registrationError is not null)
                {
                    return CreateRejectedResult(proposal, StableCode(registrationError.Code));
                }
            }

            var result = await ExecuteProposalAsync(proposal, tools, cancellation.Token).ConfigureAwait(false);
            if (AgentToolOutcomePolicy.Classify(result) != AgentToolOutcomeDisposition.Continue)
            {
                _ = await CancelRegisteredRunBestEffortAsync(result.StableCode, CancellationToken.None).ConfigureAwait(false);
                _ = FinishFailure(cancellation, Snapshot.Messages, result.StableCode,
                    "An external action has an uncertain outcome. Inspect the workspace and clear the run before continuing.");
            }
            else
            {
                _ = FinishRecoverableSetupFailure(cancellation, Snapshot.Messages, result.StableCode,
                    "External MCP action finished.");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var error = await CancelRegisteredRunBestEffortAsync("request_cancelled", CancellationToken.None)
                .ConfigureAwait(false);
            _ = FinishCancelled(cancellation, Snapshot.Messages, authorityRevoked: error is null);
            throw;
        }
        finally
        {
            ReleaseTurn(cancellation);
        }
    }

    private async ValueTask<AgentAuthorizationResult> RequestActionAuthorizationAsync(
        AgentActionProposal proposal, CancellationToken cancellationToken)
    {
        if (_externalRun)
        {
            var policy = await _workspaceLayoutPort!.ResolveExternalPolicyAsync(
                proposal.Target, proposal.Presentation.Arguments, cancellationToken).ConfigureAwait(false);
            if (policy is null)
            {
                return new AgentAuthorizationResult.Denied(new AgentAuthorizationError(
                    AgentAuthorizationErrorCode.TargetOutsideRunScope, "The external action's policy target is no longer available."));
            }
            var error = await RefreshExternalPolicyAsync(AgentPolicyResolver.Resolve(policy), cancellationToken)
                .ConfigureAwait(false);
            if (error is not null) { return new AgentAuthorizationResult.Denied(error); }
            if (proposal.PolicyGeneration != GetPolicyGeneration())
            {
                // Reuse the existing retry to recompose the same exact action
                // with its current policy generation before requesting approval.
                return new AgentAuthorizationResult.Denied(new AgentAuthorizationError(
                    AgentAuthorizationErrorCode.PolicyChanged, "The current external action policy was refreshed."));
            }
        }
        return await _broker.RequestAsync(proposal, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AgentAuthorizationError?> RefreshExternalPolicyAsync(
        AgentPolicy policy, CancellationToken cancellationToken)
    {
        AgentRunPolicyUpdate update;
        lock (_gate)
        {
            if (PolicyAuthorityEqual(_baselinePolicy, policy)) { return null; }
            update = new AgentRunPolicyUpdate(GetRequiredSession().RunId, policy,
                checked(_policyGeneration + 1), _approvalActor);
            _policyChangeInFlight = true;
        }

        var error = await UpdateRunPolicyWithAuditRecoveryAsync(update,
            "The external action policy update could not be confirmed.", cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            _ = PausePolicyChangeForRetry(update, error, "The external action policy could not be refreshed. Retry the action.");
            return error;
        }
        lock (_gate)
        {
            _baselinePolicy = policy;
            _runPolicy = policy;
            _effectivePolicy = policy;
            _policyGeneration = update.PolicyGeneration;
            _policyChangeInFlight = false;
            _pendingPolicyUpdate = null;
            _snapshot = _snapshot with
            {
                TerminalMutationPermission = policy.GetPermission(AgentCapability.RunCommands),
                EffectivePolicy = policy,
                BaselinePolicy = policy,
                RunPolicy = policy,
                PolicyGeneration = _policyGeneration,
            };
        }
        NotifyChanged();
        return null;
    }
}
