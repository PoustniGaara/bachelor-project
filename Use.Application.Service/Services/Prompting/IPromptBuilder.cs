using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Prompting;

/// <summary>
/// Builds the final prompt sent to the LLM after retrieval.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>Returns the system prompt steering the LLM behaviour.</summary>
    string BuildSystemPrompt();

    /// <summary>Builds the user-visible prompt that contains the retrieved context + question.</summary>
    string BuildUserPrompt(string question, IReadOnlyList<RetrievedChunk> chunks);
}

