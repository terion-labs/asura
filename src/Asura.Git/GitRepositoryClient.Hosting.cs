using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitRepositoryHandle>> CloneHostedRepositoryWithExecutableAsync(ConnectionProfile connection,
        GitHostingAccount account, string url, string path, string executable, CancellationToken cancellationToken)
    {
        if (!Supports(connection))
        {
            return Failure<GitRepositoryHandle>(GitErrorCode.Unsupported, "Git repositories require a local or SSH connection.");
        }
        _ = ValidateGitExecutable(executable);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var remote) || !string.Equals(remote.Scheme, "https", StringComparison.Ordinal))
        {
            return await CloneRepositoryWithExecutableAsync(connection, url, path, executable, cancellationToken).ConfigureAwait(false);
        }

        var expectedHost = account.Provider == GitHostingProvider.GitHub && string.Equals(account.ApiBase.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            ? "github.com" : account.ApiBase.Host;
        if (!string.Equals(remote.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
            || remote.UserInfo.Length > 0 || secretVault is null || !account.Reference.Value.StartsWith(HostingAccountPrefix(), StringComparison.Ordinal))
        {
            return Failure<GitRepositoryHandle>(GitErrorCode.AuthenticationRequired, "The clone address does not belong to the selected hosting account.");
        }

        var stored = await secretVault.ResolveAsync(new(account.Reference,
            new(SecretScopeKind.Connection, connection.Id.Value), new(SecretUseKind.ConnectionAuthentication, connection.Id.Value)), cancellationToken).ConfigureAwait(false);
        if (stored is not SecretVaultResult<SecretMaterial>.Success saved)
        {
            return await CloneRepositoryWithExecutableAsync(connection, url, path, executable, cancellationToken).ConfigureAwait(false);
        }

        using var material = saved.Value;
        var result = await ExecuteAuthenticatedAsync(new GitRepositoryHandle(connection, path) { Executable = executable },
            ["clone", "--", url, path], remote, material, cancellationToken, inRepository: false).ConfigureAwait(false);
        return result is GitResult<GitUnit>.Failure failure ? new GitResult<GitRepositoryHandle>.Failure(failure.Error)
            : await OpenRepositoryWithExecutableAsync(connection, path, executable, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<GitResult<IReadOnlyList<GitHostingAccount>>> ReadHostingAccountsAsync(
        ConnectionProfile connection, CancellationToken cancellationToken)
    {
        if (secretVault is null)
        {
            return new GitResult<IReadOnlyList<GitHostingAccount>>.Success([]);
        }

        var scope = new SecretScope(SecretScopeKind.Connection, connection.Id.Value);
        var purpose = new SecretUsePurpose(SecretUseKind.ConnectionAuthentication, connection.Id.Value);
        var result = await secretVault.ListMetadataAsync(new(scope, purpose), cancellationToken).ConfigureAwait(false);
        if (result is SecretVaultResult<IReadOnlyList<SecretMetadata>>.Failure)
        {
            return Failure<IReadOnlyList<GitHostingAccount>>(GitErrorCode.CommandFailed, "Could not read hosting accounts from the credential vault.");
        }

        var accounts = new List<GitHostingAccount>();
        var prefix = HostingAccountPrefix();
        foreach (var metadata in ((SecretVaultResult<IReadOnlyList<SecretMetadata>>.Success)result).Value)
        {
            if (!metadata.Reference.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = metadata.Label.Split('|');
            if (parts.Length == 4 && string.Equals(parts[0], "Git account", StringComparison.Ordinal)
                && Enum.TryParse<GitHostingProvider>(parts[1], out var provider)
                && Uri.TryCreate(parts[2], UriKind.Absolute, out var api))
            {
                accounts.Add(new(provider, api, parts[3], metadata.Reference));
            }
        }

        return new GitResult<IReadOnlyList<GitHostingAccount>>.Success(accounts);
    }

    public async ValueTask<GitResult<GitUnit>> RemoveHostingAccountAsync(ConnectionProfile connection,
        GitHostingAccount account, CancellationToken cancellationToken)
    {
        if (secretVault is null || !account.Reference.Value.StartsWith(HostingAccountPrefix(), StringComparison.Ordinal))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "This account is unavailable in this workspace.");
        }

        var result = await secretVault.DeleteAsync(new(account.Reference, new(SecretScopeKind.Connection, connection.Id.Value),
            new(SecretUseKind.ConnectionAuthentication, connection.Id.Value)), cancellationToken).ConfigureAwait(false);
        return result is SecretVaultResult<Unit>.Success ? new GitResult<GitUnit>.Success(GitUnit.Value)
            : Failure<GitUnit>(GitErrorCode.CommandFailed, "Could not remove hosting credentials.");
    }

    public async ValueTask<GitResult<GitHostedRepositoryPage>> ReadHostedRepositoriesAsync(ConnectionProfile connection,
        GitHostingProvider provider, Uri apiBase, GitHostingAccount? account, int page, bool starred,
        CancellationToken cancellationToken)
    {
        if (hostingHttpHandlerFactory is null || credentialPrompt is null)
        {
            return Failure<GitHostedRepositoryPage>(GitErrorCode.Unsupported, "Hosting access is unavailable on this connection.");
        }

        if (!string.Equals(apiBase.Scheme, "https", StringComparison.Ordinal) || apiBase.UserInfo.Length > 0 || apiBase.Query.Length > 0 || apiBase.Fragment.Length > 0 || page < 1)
        {
            return Failure<GitHostedRepositoryPage>(GitErrorCode.CommandFailed, "Use an HTTPS API base URL without credentials, query, or fragment.");
        }

        var scope = new SecretScope(SecretScopeKind.Connection, connection.Id.Value);
        var purpose = new SecretUsePurpose(SecretUseKind.ConnectionAuthentication, connection.Id.Value);
        var stored = account is not null && secretVault is not null && account.Provider == provider && account.ApiBase == apiBase
            && account.Reference.Value.StartsWith(HostingAccountPrefix(), StringComparison.Ordinal)
            ? await secretVault.ResolveAsync(new(account.Reference, scope, purpose), cancellationToken).ConfigureAwait(false) : null;
        using var saved = stored is SecretVaultResult<SecretMaterial>.Success value ? value.Value : null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var credentials = attempt == 0 && saved is not null ? null
                : await credentialPrompt.RequestAsync(apiBase, secretVault?.Availability.CanPersist == true, attempt > 0, cancellationToken).ConfigureAwait(false);
            var material = attempt == 0 && saved is not null ? saved : credentials?.Material;
            if (material is null)
            {
                return Failure<GitHostedRepositoryPage>(GitErrorCode.Cancelled, "Hosting sign-in was cancelled.");
            }

            var buffer = new byte[material.Length];
            material.CopyTo(buffer);
            try
            {
                var text = Encoding.UTF8.GetString(buffer);
                var separator = text.IndexOf('\n', StringComparison.Ordinal);
                if (separator < 1)
                {
                    return Failure<GitHostedRepositoryPage>(GitErrorCode.CommandFailed, "Stored account credentials are invalid.");
                }

                var username = text[..separator];
                var token = text[(separator + 1)..].TrimEnd('\n');
                using var client = new HttpClient(hostingHttpHandlerFactory(connection)) { Timeout = NetworkTimeout };
                var endpoint = provider == GitHostingProvider.GitHub
                    ? (starred ? "user/starred" : "user/repos") + $"?per_page=100&page={page}&sort=updated"
                    : "projects?membership=true" + (starred ? "&starred=true" : "") + $"&per_page=100&page={page}&order_by=last_activity_at";
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(apiBase.AbsoluteUri.TrimEnd('/') + "/" + endpoint));
                request.Headers.UserAgent.ParseAdd("Asura-Git/1.0");
                if (provider == GitHostingProvider.GitHub)
                {
                    request.Headers.Authorization = new("Bearer", token);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                }
                else
                {
                    request.Headers.Add("PRIVATE-TOKEN", token);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (attempt == 0)
                    {
                        continue;
                    }

                    return Failure<GitHostedRepositoryPage>(GitErrorCode.AuthenticationRequired, "The hosting provider rejected this token. Check its repository read permissions.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Failure<GitHostedRepositoryPage>(GitErrorCode.CommandFailed, $"The hosting provider returned HTTP {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var content = new MemoryStream();
                var chunk = new byte[8192];
                int length;
                while ((length = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (content.Length + length > 4 * 1024 * 1024)
                    {
                        return Failure<GitHostedRepositoryPage>(GitErrorCode.CommandFailed, "The provider response exceeds the repository browser limit.");
                    }

                    content.Write(chunk, 0, length);
                }

                using var json = JsonDocument.Parse(content.GetBuffer().AsMemory(0, (int)content.Length));
                var repositories = new List<GitHostedRepository>();
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    repositories.Add(provider == GitHostingProvider.GitHub
                        ? new(Read(item, "full_name"), Read(item, "clone_url"), Read(item, "ssh_url"), Read(item, "html_url"), item.GetProperty("private").GetBoolean())
                        : new(Read(item, "path_with_namespace"), Read(item, "http_url_to_repo"), Read(item, "ssh_url_to_repo"), Read(item, "web_url"), string.Equals(Read(item, "visibility"), "private", StringComparison.Ordinal)));
                }

                var authenticatedAccount = credentials is null ? account : null;
                if (credentials?.Save == true && secretVault?.Availability.CanPersist == true && !username.Contains('|', StringComparison.Ordinal))
                {
                    var reference = new SecretRef(HostingAccountPrefix() + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection.Id.Value + "\n" + provider + "\n" + apiBase + "\n" + username))));
                    var existing = await secretVault.GetMetadataAsync(new(reference, scope, purpose), cancellationToken).ConfigureAwait(false);
                    var persistence = existing is SecretVaultResult<SecretMetadata>.Success
                        ? await secretVault.ReplaceAsync(new(reference, scope, purpose), material, cancellationToken).ConfigureAwait(false)
                        : await secretVault.CreateAsync(new(reference, $"Git account|{provider}|{apiBase}|{username}", SecretKind.Token, scope, purpose), material, cancellationToken).ConfigureAwait(false);
                    if (persistence is SecretVaultResult<SecretMetadata>.Failure)
                    {
                        return Failure<GitHostedRepositoryPage>(GitErrorCode.CredentialStorageFailed, "Sign-in succeeded, but the account could not be saved.");
                    }
                    authenticatedAccount = new(provider, apiBase, username, reference);
                }

                return new GitResult<GitHostedRepositoryPage>.Success(new(repositories, page, repositories.Count == 100) { Account = authenticatedAccount });
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException)
            {
                return Failure<GitHostedRepositoryPage>(GitErrorCode.CommandFailed, "Could not read the hosting repository list. Check the connection and API address.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }

        return Failure<GitHostedRepositoryPage>(GitErrorCode.AuthenticationRequired, "Hosting sign-in failed.");
    }

    private string HostingAccountPrefix() => "git-hosting-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(credentialWorkspaceId?.Value ?? "host")))[..16] + "-";

    private static string Read(JsonElement element, string property) => element.GetProperty(property).GetString() ?? "";
}
