namespace shmoxy.frontend.models;

public enum BodyDiffKind
{
    /// <summary>Both sides empty — nothing to diff.</summary>
    Empty,

    /// <summary>Line-based text diff computed.</summary>
    Text,

    /// <summary>At least one side is binary — equality + sizes only.</summary>
    Binary,

    /// <summary>Bodies exceed the diffable size cap — equality + sizes only.</summary>
    TooLarge
}
