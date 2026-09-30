namespace CompanyEmployees.Domain.Entities;

public class CompanyEvent
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 50;
    public const int MaxDescriptionLength = 200;
    public const int MaxEventsPerDatePerRegion = 5;
    public const int MinYearOffset = -5;
    public const int MaxYearOffset = 10;

    public const string DefaultCategory = "General";

    public static readonly string[] Categories =
    [
        "Anniversary",
        "TownHall",
        "Wellbeing",
        "Sustainability",
        "Innovation",
        "Milestone",
        "Celebration",
        DefaultCategory
    ];

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Description { get; set; }
    public bool IsAnnual { get; set; }
    public string Category { get; set; } = DefaultCategory;
    public string? RegionCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public CompanyEvent() { }

    public CompanyEvent(
        DateOnly date,
        string title,
        string? description = null,
        bool isAnnual = false,
        string? category = null,
        string? regionCode = null,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Date = date;
        Title = title;
        Description = description;
        IsAnnual = isAnnual;
        Category = category ?? DefaultCategory;
        RegionCode = regionCode;
    }
}
