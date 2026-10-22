using System.Security.Claims;
using Jorvis.Core;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Jorvis.Api.ProjectEndpoints;

namespace Jorvis.Api;

/// <summary>The columns of a project's board. Any member can change them.</summary>
public static class LaneEndpoints
{
    private static readonly string InvalidName = $"Give the lane a name of up to {JorvisDatabaseContext.MaxLaneNameLength} characters.";

    public static IEndpointRouteBuilder MapLaneEndpoints(this IEndpointRouteBuilder app)
    {
        var lanes = app.MapGroup("/api/projects/{label}/lanes").RequireAuthorization();

        lanes.MapPost("/", CreateAsync);
        lanes.MapPut("/{id:guid}", UpdateAsync);
        lanes.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        string label, CreateLaneRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();
        if (!TryNormalizeName(request.Name, out var name))
            return Problem(InvalidName, StatusCodes.Status400BadRequest);

        var count = await db.Lanes.CountAsync(l => l.ProjectId == project.Id, ct);
        var lane = new Lane { ProjectId = project.Id, Name = name, Position = count };
        db.Lanes.Add(lane);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/projects/{project.Label}/lanes/{lane.Id}", ToResponse(lane));
    }

    private static async Task<IResult> UpdateAsync(
        string label, Guid id, UpdateLaneRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var lanes = await db.Lanes.Where(l => l.ProjectId == project.Id).OrderBy(l => l.Position).ToListAsync(ct);
        if (lanes.Find(l => l.Id == id) is not { } lane)
            return Results.NotFound();

        if (request.Name is not null)
        {
            if (!TryNormalizeName(request.Name, out var name))
                return Problem(InvalidName, StatusCodes.Status400BadRequest);
            lane.Name = name;
        }

        if (request.Position is { } position)
        {
            lanes.Remove(lane);
            lanes.Insert(Math.Clamp(position, 0, lanes.Count), lane);
            for (var i = 0; i < lanes.Count; i++)
                lanes[i].Position = i;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(lane));
    }

    private static async Task<IResult> DeleteAsync(
        string label, Guid id, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();

        var lanes = await db.Lanes.Where(l => l.ProjectId == project.Id).OrderBy(l => l.Position).ToListAsync(ct);
        if (lanes.Find(l => l.Id == id) is not { } lane)
            return Results.NotFound();
        if (lanes.Count == 1)
            return Problem("A project needs at least one lane.", StatusCodes.Status409Conflict);
        if (await db.Tasks.AnyAsync(t => t.LaneId == id, ct))
            return Problem("Move or delete the lane's tasks first.", StatusCodes.Status409Conflict);

        db.Lanes.Remove(lane);
        lanes.Remove(lane);
        for (var i = 0; i < lanes.Count; i++)
            lanes[i].Position = i;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool TryNormalizeName(string? value, out string name)
    {
        name = value?.Trim() ?? "";
        return name.Length is > 0 and <= JorvisDatabaseContext.MaxLaneNameLength;
    }

    private static LaneResponse ToResponse(Lane lane) => new(lane.Id, lane.Name, lane.Position);
}
