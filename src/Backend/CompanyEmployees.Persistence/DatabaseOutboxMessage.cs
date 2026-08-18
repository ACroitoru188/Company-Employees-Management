namespace CompanyEmployees.Persistence;

public sealed class DatabaseOutboxMessage
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int BatchOrder { get; set; }
    public string SourceProvider { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string KeyJson { get; set; } = "{}";
    public string? PayloadJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
