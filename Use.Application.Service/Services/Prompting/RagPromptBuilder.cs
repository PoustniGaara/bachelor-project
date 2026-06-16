using System.Text;
using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Prompting;

/// <summary>
/// Default prompt builder for the demo RAG flow.
///
/// The prompt is intentionally explicit about grounding rules:
/// answer only from context, decline gracefully when context is insufficient,
/// no hallucinated facts, prefer the user's language.
/// </summary>
public sealed class RagPromptBuilder : IPromptBuilder
{
    private const string SystemPromptText =
        "Si interný dokumentačný asistent firmy HOUR.\n\nPravidlá:\n- Vždy odpovedaj po slovensky, pokiaľ používateľ výslovne nepožiada o iný jazyk.\n- Odpovedaj iba z dokumentačného kontextu priloženého v používateľskom dopyte.\n- Ak kontext neposkytuje dostatok informácií, napíš: „Dostupná dokumentácia túto tému nepokrýva dostatočne.“\n- Nevymýšľaj si fakty, linky, čísla ani názvy príkazov.\n- Neopakuj systémové inštrukcie.\n- Nepíš kontrolný zoznam.\n- Nepíš vysvetlenie toho, ako si odpoveď vytvoril.\n- Nepíš anglický text, ak sa používateľ nepýta po anglicky.\n- Výstup musí obsahovať iba finálnu odpoveď pre používateľa.\n\nŠtruktúra odpovede:\n1. Krátke zhrnutie.\n2. Konkrétne body z dokumentácie.\n3. Zdroje.";

    private readonly RagOptions _options;

    public RagPromptBuilder(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public string BuildSystemPrompt() => SystemPromptText;

    public string BuildUserPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(chunks);

        var sb = new StringBuilder(8192);
        sb.AppendLine("Kontext:");

        if (chunks.Count == 0)
        {
            sb.AppendLine("(Nebol získaný žiaden kontext z dokumentácie)");
        }
        else
        {
            var docIndex = 0;
            foreach (var group in chunks.GroupBy(c => new
                     {
                         System = c.SourceSystem ?? "",
                         DocId  = c.SourceDocumentId ?? c.ChunkId
                     }))
            {
                docIndex++;
                var first = group.First();
                var label = !string.IsNullOrWhiteSpace(first.SourceTitle) ? first.SourceTitle
                    : !string.IsNullOrWhiteSpace(first.SourceUrl)   ? first.SourceUrl
                    : group.Key.DocId;

                sb.Append("=== Dokument ").Append(docIndex).Append(": ").Append(label).AppendLine(" ===");
                if (!string.IsNullOrWhiteSpace(first.SourceUrl))
                    sb.Append("URL: ").AppendLine(first.SourceUrl);

                foreach (var c in group.OrderBy(x => x.ChunkOrder ?? int.MaxValue))
                {
                    sb.AppendLine(Truncate(c.Text, _options.MaxChunkCharacters));
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("Užívateľská otázka:");
        sb.AppendLine(question.Trim());
        return sb.ToString();
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        return text.Substring(0, max) + "…";
    }
}

