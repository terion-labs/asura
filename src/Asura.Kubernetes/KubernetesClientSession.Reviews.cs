using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    private readonly object _reviewGate = new();
    private readonly Dictionary<string, PendingReview> _reviews = new(StringComparer.Ordinal);

    private sealed record PendingReview(DateTimeOffset ExpiresAt, KubernetesDrainReview? Drain = null,
        KubernetesHelmChangeRequest? Helm = null, string? ValuesJson = null, KubernetesResourceReference? HelmStorage = null);

    private void RememberReview(string token, PendingReview review)
    {
        lock (_reviewGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (string expired in _reviews.Where(static pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(static pair => pair.Key).ToArray())
            {
                _reviews.Remove(expired);
            }

            if (_reviews.Count >= 8)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.TooManyRequests, "This session already holds eight active operation reviews. Wait for a review to expire or reopen the session.");
            }

            _reviews.Add(token, review);
        }
    }

    private PendingReview ConsumeReview(string token)
    {
        lock (_reviewGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrEmpty(token) || !_reviews.Remove(token, out PendingReview? review) || review.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The operation review expired, was already used, or belongs to another session. Review the operation again.");
            }

            return review;
        }
    }
}
