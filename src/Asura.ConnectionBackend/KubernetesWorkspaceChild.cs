using Asura.Application;
using Asura.Infrastructure;
using Asura.Kubernetes;

namespace Asura.ConnectionBackend;

internal static partial class KubernetesWorkspaceChild
{
    internal static Task RunAsync(Stream input, Stream output, CancellationToken token) =>
        RunAsync(input, output, null, token);

    internal static async Task RunAsync(Stream input, Stream output,
        Func<KubernetesWorkspaceOpen, CancellationToken, Task<IKubernetesClientSession>>? open,
        CancellationToken token)
    {
        await using var channel = new KubernetesWorkspaceChannel(input, output, token);
        var first = await channel.ReadAsync(token).ConfigureAwait(false);
        if (first is { Id: 1, Operation: KubernetesWorkspaceOperation.Review, Open: { } review })
        {
            try
            {
                var result = await KubernetesWorkspaceConfiguration.ReviewAsync(review, token).ConfigureAwait(false);
                await channel.ReplyAsync(new(1, Review: result), token).ConfigureAwait(false);
            }
            catch (Exception exception) { await channel.ReplyAsync(Failure(1, exception), token).ConfigureAwait(false); }
            return;
        }
        if (first is not { Id: 1, Operation: KubernetesWorkspaceOperation.Open, Open: { } configuration })
        {
            throw new InvalidDataException("Kubernetes requires an initial configuration frame.");
        }
        IKubernetesClientSession? client = null;
        try
        {
            client = open is null
                ? await OpenAsync(configuration, channel.RefreshAsync, token).ConfigureAwait(false)
                : await open(configuration, token).ConfigureAwait(false);
            await channel.ReplyAsync(new(1), token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await channel.ReplyAsync(Failure(1, exception), token).ConfigureAwait(false);
            return;
        }
        await using (client.ConfigureAwait(false))
        {
            var lastId = 1L;
            while (!token.IsCancellationRequested)
            {
                KubernetesWorkspaceRequest request;
                try
                {
                    request = await channel.ReadAsync(token).ConfigureAwait(false);
                }
                catch (EndOfStreamException) { return; }
                if (request.Id != checked(lastId + 1) || request.Operation == KubernetesWorkspaceOperation.Open
                    || !Enum.IsDefined(request.Operation))
                {
                    throw new InvalidDataException("The Kubernetes backend request sequence is invalid.");
                }
                lastId = request.Id;
                try
                {
                    if (request.Operation == KubernetesWorkspaceOperation.Watch)
                    {
                        var watch = request.Watch ?? throw InvalidRequest();
                        await foreach (var change in client.WatchAsync(watch, token).ConfigureAwait(false))
                        {
                            await channel.ReplyAsync(new(request.Id, WatchEvent: change), token).ConfigureAwait(false);
                        }
                        await channel.ReplyAsync(new(request.Id, Completed: true), token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.FollowLogs)
                    {
                        await foreach (var chunk in client.FollowLogsAsync(request.Logs ?? throw InvalidRequest(), token).ConfigureAwait(false))
                        {
                            if (chunk.Length > 32768) { throw InvalidRequest(); }
                            await channel.ReplyAsync(new(request.Id, LogChunk: chunk), token).ConfigureAwait(false);
                        }
                        await channel.ReplyAsync(new(request.Id, Completed: true), token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.ExecStart)
                    {
                        await RunExecAsync(client, request, channel, token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.ForwardStart)
                    {
                        await RunForwardAsync(client, request, channel, token).ConfigureAwait(false);
                        return;
                    }
                    KubernetesWorkspaceResponse response = request.Operation switch
                    {
                        KubernetesWorkspaceOperation.Discover => new(request.Id,
                            Discovery: await client.DiscoverAsync(token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.List => new(request.Id,
                            Page: await client.ListAsync(request.List ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.Inspect => new(request.Id,
                            Resource: await client.InspectAsync(request.Resource ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.Logs => new(request.Id,
                            Logs: await client.ReadLogsAsync(request.Logs ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.Mutate => new(request.Id,
                            Mutation: await client.MutateAsync(request.Mutation ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.ConvertManifest => new(request.Id,
                            ManifestJson: ConvertManifest(request.Manifest ?? throw InvalidRequest())),
                        KubernetesWorkspaceOperation.Metrics => new(request.Id,
                            Metrics: await client.ReadMetricsAsync(request.Metrics ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.MetricHistory => new(request.Id,
                            MetricHistory: await client.ReadMetricHistoryAsync(request.MetricHistory ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.HelmList => new(request.Id,
                            HelmReleases: await client.ListHelmReleasesAsync(request.HelmList ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.HelmHistory => new(request.Id,
                            HelmHistory: await client.ReadHelmHistoryAsync(request.HelmHistory ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.NodeScheduling => new(request.Id,
                            Mutation: await client.SetNodeSchedulableAsync(request.NodeScheduling ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.NodeDrainReview => new(request.Id,
                            NodeDrainReview: await client.ReviewNodeDrainAsync(request.NodeDrain ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.NodeDrainExecute => new(request.Id,
                            NodeDrainResult: await client.ExecuteNodeDrainAsync(request.ReviewToken ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.HelmChangeReview => new(request.Id,
                            HelmChangeReview: await client.ReviewHelmChangeAsync(request.HelmChange ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        KubernetesWorkspaceOperation.HelmChangeExecute => new(request.Id,
                            HelmChangeResult: await client.ExecuteHelmChangeAsync(request.ReviewToken ?? throw InvalidRequest(), token).ConfigureAwait(false)),
                        _ => throw InvalidRequest(),
                    };
                    await channel.ReplyAsync(response, token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await channel.ReplyAsync(Failure(request.Id, exception), token).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<IKubernetesClientSession> OpenAsync(KubernetesWorkspaceOpen configuration, KubernetesCredentialRefresh refresh, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(configuration.ContextName)) { throw InvalidRequest(); }
        if (configuration.HostConnection is { } hostConnection)
        {
            if (configuration.KubeconfigPath is not null || configuration.ManagedKubeconfig is not null
                || configuration.TrustedExecFingerprint is not null) { throw InvalidRequest(); }
            return new KubernetesClientSession(hostConnection, refresh);
        }
        var plans = await KubernetesWorkspaceConfiguration.ReadAsync(configuration, token).ConfigureAwait(false);
        var plan = plans.SingleOrDefault(item => string.Equals(item.ContextName, configuration.ContextName, StringComparison.Ordinal))
            ?? throw InvalidRequest();
        var resolver = new KubernetesCredentialResolver(plan, configuration.TrustedExecFingerprint, new PathConnectionExecutableLocator().Find);
        var connection = await resolver.ResolveAsync(token).ConfigureAwait(false);
        return new KubernetesClientSession(connection with { Namespace = configuration.Namespace }, resolver.ResolveAsync);
    }

    private static string ConvertManifest(string manifest)
    {
        var documents = KubernetesYaml.ToJsonDocuments(manifest);
        if (documents.Count != 1) { throw InvalidRequest(); }
        return documents[0];
    }

    private static KubernetesWorkspaceResponse Failure(long id, Exception exception) => exception is KubernetesRequestException failure
        ? new(id, failure.Code, failure.StatusCode, failure.Retryable,
            ErrorMessage: failure.Message.Length <= 2048 ? failure.Message : null)
        : new(id, KubernetesErrorCode.InvalidConfiguration);

    private static KubernetesRequestException InvalidRequest() => new(KubernetesErrorCode.InvalidConfiguration,
        "The Kubernetes backend configuration or request is invalid.");
}
