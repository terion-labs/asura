using System.Text;

namespace Asura.Application;

/// <summary>Human-only authentication for a Git HTTP remote; agent operations never invoke it.</summary>
public interface IGitCredentialPrompt
{
    ValueTask<GitCredentials?> RequestAsync(
        Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken);
}

/// <summary>Owns the username/password pair until the Git invocation and optional vault write finish.</summary>
public sealed class GitCredentials(SecretMaterial material, bool save) : IDisposable
{
    public SecretMaterial Material { get; } = material;
    public bool Save { get; } = save;

    public static GitCredentials Create(string username, string password, bool save)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (username.IndexOfAny(['\r', '\n', '\0']) >= 0 || password.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("Git credentials cannot contain line breaks or NUL characters.");
        }

        return new GitCredentials(SecretMaterial.TakeOwnership(Encoding.UTF8.GetBytes($"{username}\n{password}\n")), save);
    }

    public void Dispose() => Material.Dispose();
    public override string ToString() => "[Git credentials]";
}
