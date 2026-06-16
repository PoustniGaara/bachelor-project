using System.Text.Json;
using System.Text.Json.Serialization;

namespace Use.Application.Service.Evaluation.Models;

/// <summary>
/// The pipeline stage at which an answerable evaluation case lost its expected
/// source. Serialized to the report as snake_case strings (e.g.
/// <c>missed_after_fusion</c>).
/// </summary>
[JsonConverter(typeof(EvaluationFailureStageJsonConverter))]
public enum EvaluationFailureStage
{
    /// <summary>The expected source survived into the final assembled context.</summary>
    Passed,

    /// <summary>Neither pass found it and the semantic pass is the prime suspect.</summary>
    MissedBySemantic,

    /// <summary>Neither pass found it and the lexical pass is the prime suspect.</summary>
    MissedByLexical,

    /// <summary>Found by a pass but absent from the fused candidate set.</summary>
    MissedAfterFusion,

    /// <summary>Present after fusion but dropped from the reranked candidate set.</summary>
    LostAfterRerank,

    /// <summary>Survived reranking but the target document was not selected.</summary>
    LostInDocumentSelection,

    /// <summary>Document selected but the target chunk did not reach the context.</summary>
    LostInContextAssembly,

    /// <summary>The case has no target chunk/document, so retrieval recall is N/A.</summary>
    NoExpectedSourceDefined,

    /// <summary>Something threw while evaluating the case.</summary>
    EvaluationError
}

/// <summary>Maps <see cref="EvaluationFailureStage"/> values to their wire strings.</summary>
public static class EvaluationFailureStageExtensions
{
    public static string ToWireString(this EvaluationFailureStage stage) => stage switch
    {
        EvaluationFailureStage.Passed => "passed",
        EvaluationFailureStage.MissedBySemantic => "missed_by_semantic",
        EvaluationFailureStage.MissedByLexical => "missed_by_lexical",
        EvaluationFailureStage.MissedAfterFusion => "missed_after_fusion",
        EvaluationFailureStage.LostAfterRerank => "lost_after_rerank",
        EvaluationFailureStage.LostInDocumentSelection => "lost_in_document_selection",
        EvaluationFailureStage.LostInContextAssembly => "lost_in_context_assembly",
        EvaluationFailureStage.NoExpectedSourceDefined => "no_expected_source_defined",
        EvaluationFailureStage.EvaluationError => "evaluation_error",
        _ => stage.ToString()
    };
}

/// <summary>JSON converter that reads/writes <see cref="EvaluationFailureStage"/> as snake_case.</summary>
public sealed class EvaluationFailureStageJsonConverter : JsonConverter<EvaluationFailureStage>
{
    public override EvaluationFailureStage Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "passed" => EvaluationFailureStage.Passed,
            "missed_by_semantic" => EvaluationFailureStage.MissedBySemantic,
            "missed_by_lexical" => EvaluationFailureStage.MissedByLexical,
            "missed_after_fusion" => EvaluationFailureStage.MissedAfterFusion,
            "lost_after_rerank" => EvaluationFailureStage.LostAfterRerank,
            "lost_in_document_selection" => EvaluationFailureStage.LostInDocumentSelection,
            "lost_in_context_assembly" => EvaluationFailureStage.LostInContextAssembly,
            "no_expected_source_defined" => EvaluationFailureStage.NoExpectedSourceDefined,
            _ => EvaluationFailureStage.EvaluationError
        };
    }

    public override void Write(
        Utf8JsonWriter writer, EvaluationFailureStage value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToWireString());
}

