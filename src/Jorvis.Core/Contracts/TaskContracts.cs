namespace Jorvis.Core.Contracts;

public record TaskResponse(Guid Id, string Title, string? Notes, DateOnly? DueDate, bool IsDone, DateTimeOffset CreatedAt);

public record CreateTaskRequest(string Title, string? Notes, DateOnly? DueDate);

public record UpdateTaskRequest(string Title, string? Notes, DateOnly? DueDate, bool IsDone);
