namespace Use.Application.Service.Evaluation.Models;

/// <summary>
/// Whether the expected target was found at one retrieval stage, and if so, at
/// which rank. <see cref="Applicable"/> is false when the case defines no target
/// usable for that stage (so it must not count toward recall).
/// </summary>
public sealed class RetrievalStageHitInfo
{
    /// <summary>True when the case defines a target usable by this stage.</summary>
    public bool Applicable { get; set; }

    /// <summary>True when the expected target appeared in this stage's candidates.</summary>
    public bool Hit { get; set; }

    /// <summary>Best (smallest) 1-based rank of the matched candidate, or null.</summary>
    public int? BestRank { get; set; }

    /// <summary>The chunk/document id that matched, or null.</summary>
    public string? MatchedId { get; set; }

    /// <summary>How the match was made: <c>chunk</c>, <c>document</c>, or <c>none</c>.</summary>
    public string MatchLevel { get; set; } = "none";

    /// <summary>How many candidates this stage produced.</summary>
    public int CandidateCount { get; set; }
}

