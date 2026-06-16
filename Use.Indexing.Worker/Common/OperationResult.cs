namespace Use.Indexing.Worker.Common;

/// <summary>
/// Lightweight result wrapper used across the indexing pipeline so individual
/// stages can signal success/failure without throwing. Throwing is reserved
/// for unexpected/infrastructural errors; expected per-document failures are
/// surfaced via <see cref="OperationResult{T}"/> so the orchestrator can decide
/// whether to skip a document or abort.
/// </summary>
public readonly record struct OperationResult<T>(bool Success, T? Value, string? Error)
{
    public static OperationResult<T> Ok(T value) => new(true, value, null);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
}

