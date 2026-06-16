namespace Use.Application.Service.Configuration;

/// <summary>
/// Maps <see cref="RetrievalMode"/> to the string persisted in
/// <c>rag_query_log.retrieval_strategy</c> (values aligned with the examples in
/// <c>create.sql</c>).
/// </summary>
public static class RetrievalStrategyNames
{
    public const string Semantic = "semantic";
    public const string Lexical = "lexical";
    public const string HybridRrf = "hybrid_rrf";

    public static string From(RetrievalMode mode) => mode switch
    {
        RetrievalMode.SemanticOnly => Semantic,
        RetrievalMode.LexicalOnly => Lexical,
        RetrievalMode.Hybrid => HybridRrf,
        _ => HybridRrf
    };
}

