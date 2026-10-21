namespace Asura.Infrastructure;

public interface IConnectionExecutableLocator
{
    string? Find(string executable);

    /// <summary>
    /// The directories <see cref="Find"/> searches, as a PATH value, or null
    /// when the inherited PATH is already the whole search.
    /// </summary>
    string? SearchPath => null;
}
