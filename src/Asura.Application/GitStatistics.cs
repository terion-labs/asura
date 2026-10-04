namespace Asura.Git;

public sealed record GitDirectorySize(string Path, long Bytes, int Files);
public sealed record GitRepositoryStatistics(IReadOnlyList<GitDirectorySize> Directories, string Contributors);
