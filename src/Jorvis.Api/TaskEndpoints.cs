using System.Security.Claims;
using Jorvis.Core;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Jorvis.Api.ProjectEndpoints;

namespace Jorvis.Api;

/// <summary>A project's tasks, addressed by their number within the project. Any member can change them.</summary>
public static class TaskEndpoints
{
    private static readonly string InvalidTitle = $"Give the task a title of up to {JorvisDatabaseContext.MaxTaskTitleLength} characters.";
    private static readonly string InvalidDescription = $"Keep the description under {JorvisDatabaseContext.MaxTaskDescriptionLength} characters.";

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/projects/{label}/tasks").RequireAuthorization();

        tasks.MapGet("/", ListAsync);
        tasks.MapGet("/{number:int}", GetAsync);
        tasks.MapPost("/", CreateAsync);
        tasks.MapPut("/{number:int}", UpdateAsync);
        tasks.MapPost("/{number:int}/move", MoveAsync);
        tasks.MapDelete("/{number:int}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(string label, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == project.Id)
            .OrderBy(t => t.Position).ThenBy(t => t.Number)
            .ToListAsync(ct);
        return Results.Ok(tasks.Select(t => ToResponse(project, t)));
    }

    private static async Task<IResult> GetAsync(string label, int number, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
        return task is null ? Results.NotFound() : Results.Ok(ToResponse(project, task));
    }

    private static async Task<IResult> CreateAsync(
        string label, CreateTaskRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();
        if (Validate(request.Title, request.Description, out var title, out var description) is { } invalid)
            return invalid;

        var lane = request.LaneId is { } laneId
            ? await db.Lanes.SingleOrDefaultAsync(l => l.ProjectId == project.Id && l.Id == laneId, ct)
            : await db.Lanes.Where(l => l.ProjectId == project.Id).OrderBy(l => l.Position).FirstOrDefaultAsync(ct);
        if (lane is null)
            return Problem("That lane is not on this board.", StatusCodes.Status400BadRequest);

        var task = new TaskItem
        {
            ProjectId = project.Id,
            LaneId = lane.Id,
            Position = await db.Tasks.CountAsync(t => t.LaneId == lane.Id, ct),
            Title = title,
            Description = description
        };

        // Take the project's next number. Two tasks created at once both try to take the same one; the concurrency token
        // on NextTaskNumber lets only one through, and the other takes the number after it.
        for (var attempt = 1; ; attempt++)
        {
            task.Number = project.NextTaskNumber++;
            db.Tasks.Add(task);
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                db.Entry(task).State = EntityState.Detached;
                await db.Entry(project).ReloadAsync(ct);
            }
        }

        return Results.Created($"/api/projects/{project.Label}/tasks/{task.Number}", ToResponse(project, task));
    }

    private static async Task<IResult> UpdateAsync(
        string label, int number, UpdateTaskRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();
        if (Validate(request.Title, request.Description, out var title, out var description) is { } invalid)
            return invalid;

        var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
        if (task is null)
            return Results.NotFound();

        task.Title = title;
        task.Description = description;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(project, task));
    }

    private static async Task<IResult> MoveAsync(
        string label, int number, MoveTaskRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
        if (task is null)
            return Results.NotFound();
        if (!await db.Lanes.AnyAsync(l => l.ProjectId == project.Id && l.Id == request.LaneId, ct))
            return Problem("That lane is not on this board.", StatusCodes.Status400BadRequest);

        // Renumber both lanes so positions stay 0, 1, 2... with no gaps or ties.
        var from = await LaneTasksAsync(db, task.LaneId, task.Id, ct);
        var to = request.LaneId == task.LaneId ? from : await LaneTasksAsync(db, request.LaneId, task.Id, ct);
        to.Insert(Math.Clamp(request.Position, 0, to.Count), task);
        task.LaneId = request.LaneId;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        Renumber(from);
        Renumber(to);

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(project, task));
    }

    private static async Task<IResult> DeleteAsync(string label, int number, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var task = await db.Tasks.SingleOrDefaultAsync(t => t.ProjectId == project.Id && t.Number == number, ct);
        if (task is null)
            return Results.NotFound();

        db.Tasks.Remove(task);
        Renumber(await LaneTasksAsync(db, task.LaneId, task.Id, ct));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static IResult? Validate(string? titleValue, string? descriptionValue, out string title, out string? description)
    {
        title = titleValue?.Trim() ?? "";
        description = string.IsNullOrWhiteSpace(descriptionValue) ? null : descriptionValue.Trim();

        if (title.Length is 0 or > JorvisDatabaseContext.MaxTaskTitleLength)
            return Problem(InvalidTitle, StatusCodes.Status400BadRequest);
        if (description?.Length > JorvisDatabaseContext.MaxTaskDescriptionLength)
            return Problem(InvalidDescription, StatusCodes.Status400BadRequest);
        return null;
    }

    /// <summary>A lane's tasks in order, leaving out one task (the one being moved or deleted).</summary>
    private static async Task<List<TaskItem>> LaneTasksAsync(JorvisDatabaseContext db, Guid laneId, Guid except, CancellationToken ct) =>
        await db.Tasks
            .Where(t => t.LaneId == laneId && t.Id != except)
            .OrderBy(t => t.Position).ThenBy(t => t.Number)
            .ToListAsync(ct);

    private static void Renumber(List<TaskItem> tasks)
    {
        for (var i = 0; i < tasks.Count; i++)
            tasks[i].Position = i;
    }

    private static TaskResponse ToResponse(Project project, TaskItem task) =>
        new(task.Number, $"{project.Label}-{task.Number}", task.LaneId, task.Position, task.Title, task.Description,
            task.CreatedAt, task.UpdatedAt);
}
