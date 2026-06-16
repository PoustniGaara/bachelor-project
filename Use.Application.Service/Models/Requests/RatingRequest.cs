using System.ComponentModel.DataAnnotations;

namespace Use.Application.Service.Models.Requests;

/// <summary>
/// Body of the rating endpoints
/// (<c>POST /api/chat/rag-query-logs/{ragQueryLogId}/rating</c> and
/// <c>POST /api/chat/messages/{messageId}/rating</c>).
/// </summary>
public sealed class RatingRequest
{
    /// <summary>
    /// User feedback signal: <c>-1</c> = bad, <c>1</c> = good, <c>null</c> = clear rating.
    /// </summary>
    [Range(-1, 1)]
    public short? Rating { get; set; }

    /// <summary>Optional free-text feedback.</summary>
    public string? Feedback { get; set; }
}

