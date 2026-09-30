namespace Asura.Core;

/// <summary>An inert occurrence of hidden chat text, never execution authority.</summary>
public sealed record ChatHiddenReference
{
    public ChatHiddenReference(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("A hidden chat reference must be a random identifier.", nameof(id));
        }
        Id = id;
    }

    public string Id { get; }
    public string Placeholder => "⟦hidden-" + Id + "⟧";
}
