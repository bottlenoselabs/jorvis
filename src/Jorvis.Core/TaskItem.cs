namespace Jorvis.Core;

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsDone { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
