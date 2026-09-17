using Asura.Application;
using Asura.Kubernetes;

namespace Asura.ConnectionBackend;

internal static partial class KubernetesWorkspaceChild
{
    internal static Task RunAsync(Stream input, Stream output, CancellationToken token) =>
        RunAsync(input, output, OpenAsync, token);

    internal static async Task RunAsync(Stream input, Stream output,
        Func<KubernetesWorkspaceOpen, CancellationToken, Task<IKubernetesClientSession>> open,
        CancellationToken token)
    {
        var first = await BackendJsonFrames.ReadAsync(input,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, token).ConfigureAwait(false);
        if (first is { Id: 1, Operation: KubernetesWorkspaceOperation.Review, Open: { } review })
        {
            try
            {
                var plans = await ReadPlansAsync(review, token).ConfigureAwait(false);
                var contexts = plans.Select(plan => new KubernetesContextReview(plan.ContextName, plan.Connection.Namespace,
                    plan.Connection.ApiServer.AbsoluteUri, plan.Exec is not null ? "Credential executable"
                        : plan.Connection.ClientCertificatePem is not null || plan.ClientCertificatePath is not null ? "Client certificate"
                        : plan.Connection.BearerToken is not null || plan.TokenFilePath is not null ? "Bearer token" : "Anonymous",
                    plan.Connection.AllowInsecureTls, plan.Exec?.Command, plan.Exec?.Arguments ?? [],
                    plan.Exec?.Environment.Keys.ToArray() ?? [], plan.Exec?.Fingerprint)).ToArray();
                await ReplyAsync(new(1, Review: new(contexts)), output, token).ConfigureAwait(false);
            }
            catch (Exception exception) { await ReplyAsync(Failure(1, exception), output, token).ConfigureAwait(false); }
            return;
        }
        if (first is not { Id: 1, Operation: KubernetesWorkspaceOperation.Open, Open: { } configuration })
        {
            throw new InvalidDataException("Kubernetes requires an initial configuration frame.");
        }
        IKubernetesClientSession? client = null;
        try
        {
            client = await open(configuration, token).ConfigureAwait(false);
            await ReplyAsync(new(1), output, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ReplyAsync(Failure(1, exception), output, token).ConfigureAwait(false);
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
                    request = await BackendJsonFrames.ReadAsync(input,
                        KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, token).ConfigureAwait(false);
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
                            await ReplyAsync(new(request.Id, WatchEvent: change), output, token).ConfigureAwait(false);
                        }
                        await ReplyAsync(new(request.Id, Completed: true), output, token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.FollowLogs)
                    {
                        await foreach (var chunk in client.FollowLogsAsync(request.Logs ?? throw InvalidRequest(), token).ConfigureAwait(false))
                        {
                            if (chunk.Length > 32768) { throw InvalidRequest(); }
                            await ReplyAsync(new(request.Id, LogChunk: chunk), output, token).ConfigureAwait(false);
                        }
                        await ReplyAsync(new(request.Id, Completed: true), output, token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.ExecStart)
                    {
                        await RunExecAsync(client, request, input, output, token).ConfigureAwait(false);
                        return;
                    }
                    if (request.Operation == KubernetesWorkspaceOperation.ForwardStart)
                    {
                        await RunForwardAsync(client, request, input, output, token).ConfigureAwait(false);
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
                    await ReplyAsync(response, output, token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await ReplyAsync(Failure(request.Id, exception), output, token).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<IKubernetesClientSession> OpenAsync(KubernetesWorkspaceOpen configuration, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(configuration.ContextName)) { throw InvalidRequest(); }
        var plans = await ReadPlansAsync(configuration, token).ConfigureAwait(false);
        var plan = plans.SingleOrDefault(item => string.Equals(item.ContextName, configuration.ContextName, StringComparison.Ordinal))
            ?? throw InvalidRequest();
        var resolver = new KubernetesCredentialResolver(plan, configuration.TrustedExecFingerprint);
        var connection = await resolver.ResolveAsync(token).ConfigureAwait(false);
        return new KubernetesClientSession(connection with { Namespace = configuration.Namespace }, resolver.ResolveAsync);
    }

    private static async Task<IReadOnlyList<KubernetesKubeconfigPlan>> ReadPlansAsync(KubernetesWorkspaceOpen configuration, CancellationToken token)
    {
        if ((configuration.KubeconfigPath is null) == (configuration.ManagedKubeconfig is null))
        {
            throw InvalidRequest();
        }
        var path = configuration.KubeconfigPath;
        if (path is not null)
        {
            if (string.Equals(path, "~", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");
            }
            path = Path.GetFullPath(path);
        }
        var yaml = configuration.ManagedKubeconfig ?? await ReadConfigurationAsync(path!, token).ConfigureAwait(false);
        return KubernetesKubeconfigReader.Read(yaml, path is null ? null : Path.GetDirectoryName(path));
    }

    private static string ConvertManifest(string manifest)
    {
        var documents = KubernetesYaml.ToJsonDocuments(manifest);
        if (documents.Count != 1) { throw InvalidRequest(); }
        return documents[0];
    }

    private static async Task<string> ReadConfigurationAsync(string path, CancellationToken token)
    {
        const int maximumBytes = 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) { throw InvalidRequest(); }
        var bytes = new byte[maximumBytes + 1];
        try
        {
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                if (read == 0) { break; }
                count += read;
            }
            if (count > maximumBytes) { throw InvalidRequest(); }
            return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private static Task ReplyAsync(KubernetesWorkspaceResponse response, Stream output, CancellationToken token) =>
        BackendJsonFrames.WriteAsync(output, response with { IsResponse = true }, KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, token);

    private static KubernetesWorkspaceResponse Failure(long id, Exception exception) => exception is KubernetesRequestException failure
        ? new(id, failure.Code, failure.StatusCode, failure.Retryable)
        : new(id, KubernetesErrorCode.InvalidConfiguration);

    private static KubernetesRequestException InvalidRequest() => new(KubernetesErrorCode.InvalidConfiguration,
        "The Kubernetes backend configuration or request is invalid.");
}
