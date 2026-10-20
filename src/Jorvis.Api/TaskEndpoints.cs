using Jorvis.Core;
using Jorvis.Core.Contracts;

namespace Jorvis.Api;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/tasks");

        tasks.MapGet("/", async (ITaskRepository repo, CancellationToken ct) =>
        {
            var items = await repo.GetAllAsync(ct);
            return Results.Ok(items.Select(ToResponse));
        });

        tasks.MapGet("/{id:guid}", async (Guid id, ITaskRepository repo, CancellationToken ct) =>
        {
            var task = await repo.GetByIdAsync(id, ct);
            return task is null ? Results.NotFound() : Results.Ok(ToResponse(task));
        });

        tasks.MapPost("/", async (CreateTaskRequest request, ITaskRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["Title"] = ["Title is required."] });

            var task = new TaskItem
            {
                Title = request.Title,
                Notes = request.Notes,
                DueDate = request.DueDate
            };
            await repo.AddAsync(task, ct);
            await repo.SaveChangesAsync(ct);
            return Results.Created($"/api/tasks/{task.Id}", ToResponse(task));
        });

        tasks.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, ITaskRepository repo, CancellationToken ct) =>
        {
            var task = await repo.GetByIdAsync(id, ct);
            if (task is null) return Results.NotFound();

            task.Title = request.Title;
            task.Notes = request.Notes;
            task.DueDate = request.DueDate;
            task.IsDone = request.IsDone;
            await repo.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(task));
        });

        tasks.MapDelete("/{id:guid}", async (Guid id, ITaskRepository repo, CancellationToken ct) =>
        {
            var task = await repo.GetByIdAsync(id, ct);
            if (task is null) return Results.NotFound();

            repo.Remove(task);
            await repo.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static TaskResponse ToResponse(TaskItem task) =>
        new(task.Id, task.Title, task.Notes, task.DueDate, task.IsDone, task.CreatedAt);
}
