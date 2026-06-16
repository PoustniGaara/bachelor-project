using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Parsing;

namespace Use.Indexing.Worker.Normalization;

/// <summary>
/// Cleans/standardizes parsed text into a final <see cref="NormalizedDocument"/>:
/// whitespace collapse, unicode normalization, casing rules, etc. Kept as a
/// separate stage from parsing because rules are content-agnostic and reused.
/// </summary>
public interface ITextNormalizer
{
    NormalizedDocument Normalize(ParsedDocument parsed);
}

