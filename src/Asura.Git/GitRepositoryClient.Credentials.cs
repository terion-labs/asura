using System.Security.Cryptography;
using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    // The helper is restricted to the challenged origin. Secrets arrive over stdin and live
    // only in the child environment, never in command arguments, Git config, or a temp file.
    private const string CredentialScript = """
        IFS= read -r ASURA_GIT_USERNAME || exit 1
        IFS= read -r ASURA_GIT_PASSWORD || exit 1
        ASURA_GIT_PROTOCOL=$1; ASURA_GIT_HOST=$2; ASURA_GIT_DEFAULT_HOST=$3; shift 3
        export ASURA_GIT_USERNAME ASURA_GIT_PASSWORD ASURA_GIT_PROTOCOL ASURA_GIT_HOST ASURA_GIT_DEFAULT_HOST
        export GIT_TERMINAL_PROMPT=0 GIT_ASKPASS=/usr/bin/false
        exec git -c credential.helper= -c 'credential.helper=!f() {
          [ "$1" = get ] || exit 0
          protocol= host=
          while IFS= read -r line && [ -n "$line" ]; do
            case "$line" in protocol=*) protocol=${line#protocol=};; host=*) host=${line#host=};; esac
          done
          [ "$protocol" = "$ASURA_GIT_PROTOCOL" ] || exit 0
          host=$(printf "%s" "$host" | tr "[:upper:]" "[:lower:]")
          [ "$host" = "$ASURA_GIT_HOST" ] || [ "$host" = "$ASURA_GIT_DEFAULT_HOST" ] || exit 0
          printf "username=%s\npassword=%s\n" "$ASURA_GIT_USERNAME" "$ASURA_GIT_PASSWORD"
        }; f' "$@" </dev/null
        """;

    private static Uri? AuthenticationRemote(GitError error)
    {
        if (error.Code != GitErrorCode.CommandFailed)
        {
            return null;
        }

        string[] markers = ["could not read Username for '", "could not read Password for '", "Authentication failed for '"];
        foreach (var marker in markers)
        {
            var start = error.Message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            var end = error.Message.IndexOf('\'', start);
            if (end > start && Uri.TryCreate(error.Message[start..end], UriKind.Absolute, out var remote)
                && remote.Scheme is "https" or "http")
            {
                return new UriBuilder(remote) { Password = "", Query = "", Fragment = "" }.Uri;
            }
        }

        return null;
    }

    private async ValueTask<GitResult<GitUnit>> AuthenticateAsync(
        GitRepositoryHandle repository, IReadOnlyList<string> arguments, Uri remote, CancellationToken token)
    {
        // Isolates can reuse the same connection ID and filesystem path. Use the durable
        // workspace ID so credentials survive reopening without crossing isolate boundaries.
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{credentialWorkspaceId?.Value ?? "host"}\n{repository.Connection.Id.Value}\n{repository.WorkingTreeRoot}\n{remote.GetLeftPart(UriPartial.Authority)}")));
        var reference = new SecretRef($"git-https-{identity}");
        var scope = new SecretScope(SecretScopeKind.Connection, repository.Connection.Id.Value);
        var purpose = new SecretUsePurpose(SecretUseKind.ConnectionAuthentication, repository.Connection.Id.Value);
        var stored = secretVault is null ? null : await secretVault.ResolveAsync(
            new ResolveSecretRequest(reference, scope, purpose), token).ConfigureAwait(false);
        var rejected = false;
        if (stored is SecretVaultResult<SecretMaterial>.Success saved)
        {
            using var material = saved.Value;
            var attempt = await ExecuteAuthenticatedAsync(repository, arguments, remote, material, token).ConfigureAwait(false);
            if (attempt is not GitResult<GitUnit>.Failure { Error.Code: GitErrorCode.AuthenticationRequired })
            {
                return attempt;
            }

            rejected = true;
        }

        using var credentials = await credentialPrompt!.RequestAsync(
            remote, secretVault?.Availability.CanPersist == true, rejected, token).ConfigureAwait(false);
        if (credentials is null)
        {
            return Failure<GitUnit>(GitErrorCode.Cancelled, "Git sign-in was cancelled.");
        }

        var result = await ExecuteAuthenticatedAsync(repository, arguments, remote, credentials.Material, token).ConfigureAwait(false);
        if (result is GitResult<GitUnit>.Success && credentials.Save && secretVault?.Availability.CanPersist == true)
        {
            var persisted = rejected
                ? await secretVault.ReplaceAsync(new ReplaceSecretRequest(reference, scope, purpose), credentials.Material, token).ConfigureAwait(false)
                : await secretVault.CreateAsync(new CreateSecretRequest(reference, $"Git {remote.Authority}", SecretKind.Password, scope, purpose), credentials.Material, token).ConfigureAwait(false);
            if (persisted is SecretVaultResult<SecretMetadata>.Failure)
            {
                // Do not invite a retry of a successful push just because persistence failed.
                return Failure<GitUnit>(GitErrorCode.CredentialStorageFailed,
                    "The Git operation succeeded, but the credentials could not be saved.");
            }
        }

        return result;
    }

    private async ValueTask<GitResult<GitUnit>> ExecuteAuthenticatedAsync(
        GitRepositoryHandle repository, IReadOnlyList<string> arguments, Uri remote, SecretMaterial material, CancellationToken token)
    {
        string[] invocation = ["-c", CredentialScript, "asura-git-auth", remote.Scheme, remote.Authority,
            remote.IsDefaultPort ? $"{remote.Authority}:{remote.Port}" : remote.Authority,
            "--literal-pathspecs", "-C", repository.WorkingTreeRoot, .. arguments];
        var command = new ConnectionCommand(repository.Connection,
            repository.RunAsUser is null ? "/bin/sh" : "sudo",
            repository.RunAsUser is { } owner
                ? ["-n", "-u", owner, "-H", .. WorkspaceSudoEnvironment(repository), "--", "/bin/sh", .. invocation]
                : invocation,
            NetworkTimeout, ReadOutputLimit)
        { StandardInput = material };
        var result = await executor.ExecuteAsync(command, token).ConfigureAwait(false);
        if (result is { Outcome: ConnectionCommandOutcome.Exited, ExitCode: 0 })
        {
            return new GitResult<GitUnit>.Success(GitUnit.Value);
        }

        // Helpers and remote servers can echo credentials. Never surface their raw output.
        var error = NonSuccessError(result);
        return new GitResult<GitUnit>.Failure(error with
        {
            Code = AuthenticationRemote(error) is not null ? GitErrorCode.AuthenticationRequired : error.Code,
            Message = "Git could not complete the authenticated operation. Check your credentials and repository access.",
        });
    }
}
