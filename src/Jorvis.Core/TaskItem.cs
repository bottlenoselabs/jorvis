namespace Jorvis.Core;

/// <summary>A ticket on a project's board, known as <c>{label}-{number}</c>, such as <c>home-reno-12</c>.</summary>
public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    /// <summary>Unique within the project and never reused.</summary>
    public int Number { get; set; }
    public Guid LaneId { get; set; }
    /// <summary>Order within the lane, top to bottom, starting at 0.</summary>
    public int Position { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
