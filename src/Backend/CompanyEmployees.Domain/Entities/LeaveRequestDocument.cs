using System;

namespace CompanyEmployees.Domain.Entities;

public class LeaveRequestDocument
{
    public Guid Id { get; set; }
    public Guid LeaveRequestId { get; set; }
    public string OriginalFileName { get; set; } = null!;
    public string HashedFileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    
    public virtual LeaveRequest LeaveRequest { get; set; } = null!;
}
