namespace Use.Application.Service.Configuration;

/// <summary>
/// Selects how the orchestrator gathers candidate chunks before the
/// document-selection / context-expansion stages.
/// </summary>
public enum RetrievalMode
{
    /// <summary>Legacy behaviour: Qdrant vector similarity only.</summary>
    SemanticOnly,

    /// <summary>PostgreSQL full-text (lexical) search only. Useful for debugging.</summary>
    LexicalOnly,

    /// <summary>Vector + lexical retrieval fused with Reciprocal Rank Fusion (default).</summary>
    Hybrid
}

