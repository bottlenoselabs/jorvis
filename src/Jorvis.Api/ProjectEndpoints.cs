using System.Security.Claims;
using Jorvis.Core;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Data;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jorvis.Api;

/// <summary>
/// Projects, who is in them, and invitations to join them. A project is only visible to its members: to anyone else it
/// answers 404, the same as one that does not exist.
/// </summary>
public static class ProjectEndpoints
{
    private const string NotOwner = "Only the project's owner can do that.";

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/api/projects").RequireAuthorization();

        projects.MapGet("/", ListAsync);
        projects.MapPost("/", CreateAsync);
        projects.MapGet("/{label}", GetAsync);
        projects.MapPut("/{label}", RenameAsync);
        projects.MapDelete("/{label}", DeleteAsync);
        projects.MapPost("/{label}/invitations", InviteAsync);
        projects.MapDelete("/{label}/invitations/{userName}", CancelInvitationAsync);
        projects.MapDelete("/{label}/members/{userName}", RemoveMemberAsync);

        // The signed-in user's own invitations: accepting one is how they join a project.
        var invitations = app.MapGroup("/api/invitations").RequireAuthorization();

        invitations.MapGet("/", ListInvitationsAsync);
        invitations.MapPost("/{label}/accept", AcceptAsync);
        invitations.MapDelete("/{label}", DeclineAsync);

        return app;
    }

    /// <summary>The project with this label, if the signed-in user is a member of it.</summary>
    internal static async Task<(Project Project, ProjectMember Member)?> FindMembershipAsync(
        JorvisDatabaseContext db, string label, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!ProjectLabels.TryNormalize(label, out label))
            return null;

        var userId = user.GetUserId();
        var found = await db.ProjectMembers
            .Where(m => m.UserId == userId)
            .Join(db.Projects.Where(p => p.Label == label), m => m.ProjectId, p => p.Id, (m, p) => new { Project = p, Member = m })
            .SingleOrDefaultAsync(ct);

        return found is null ? null : (found.Project, found.Member);
    }

    internal static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    internal static IResult Problem(string detail, int statusCode) => Results.Problem(detail, statusCode: statusCode);

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        var userId = user.GetUserId();
        var projects = await db.ProjectMembers
            .Where(m => m.UserId == userId)
            .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new ProjectSummaryResponse(
                p.Id, p.Label, p.Name, m.Role, p.Members.Count))
            .ToListAsync(ct);

        return Results.Ok(projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase));
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (!TryNormalizeName(request.Name, out var name))
            return Problem($"Give the project a name of up to {JorvisDatabaseContext.MaxProjectNameLength} characters.", StatusCodes.Status400BadRequest);
        if (!ProjectLabels.TryNormalize(request.Label, out var label))
            return Problem(ProjectLabels.Invalid, StatusCodes.Status400BadRequest);
        if (await db.Projects.AnyAsync(p => p.Label == label, ct))
            return Problem(ProjectLabels.Taken, StatusCodes.Status409Conflict);

        var project = new Project
        {
            Label = label,
            Name = name,
            Members = [new ProjectMember { UserId = user.GetUserId(), Role = ProjectRole.Owner }],
            Lanes = [.. Lane.Defaults.Select((lane, i) => new Lane { Name = lane, Position = i })]
        };
        db.Projects.Add(project);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Someone else took the label since the check above (the unique index caught it).
            return Problem(ProjectLabels.Taken, StatusCodes.Status409Conflict);
        }

        return Results.Created($"/api/projects/{project.Label}", await ToResponseAsync(db, project, ProjectRole.Owner, ct));
    }

    private static async Task<IResult> GetAsync(string label, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct) =>
        await FindMembershipAsync(db, label, user, ct) is (var project, var member)
            ? Results.Ok(await ToResponseAsync(db, project, member.Role, ct))
            : Results.NotFound();

    private static async Task<IResult> RenameAsync(
        string label, RenameProjectRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, var member))
            return Results.NotFound();
        if (member.Role != ProjectRole.Owner)
            return Problem(NotOwner, StatusCodes.Status403Forbidden);
        if (!TryNormalizeName(request.Name, out var name))
            return Problem($"Give the project a name of up to {JorvisDatabaseContext.MaxProjectNameLength} characters.", StatusCodes.Status400BadRequest);

        project.Name = name;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(db, project, member.Role, ct));
    }

    private static async Task<IResult> DeleteAsync(string label, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, var member))
            return Results.NotFound();
        if (member.Role != ProjectRole.Owner)
            return Problem(NotOwner, StatusCodes.Status403Forbidden);

        // Tasks refer to lanes without cascading (so a lane with tasks can't be deleted), so remove the tasks first.
        await db.Tasks.Where(t => t.ProjectId == project.Id).ExecuteDeleteAsync(ct);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> InviteAsync(
        string label, InviteRequest request, ClaimsPrincipal user, JorvisDatabaseContext db, UserManager<AppUser> users,
        CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, var member))
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.UserName) || await users.FindByNameAsync(request.UserName.Trim()) is not { } invitee)
            return Problem("There is no one with that username.", StatusCodes.Status404NotFound);
        if (await db.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id && m.UserId == invitee.Id, ct))
            return Problem($"{invitee.UserName} is already a member.", StatusCodes.Status409Conflict);
        if (await db.ProjectInvitations.AnyAsync(i => i.ProjectId == project.Id && i.UserId == invitee.Id, ct))
            return Problem($"{invitee.UserName} is already invited.", StatusCodes.Status409Conflict);

        db.ProjectInvitations.Add(new ProjectInvitation { ProjectId = project.Id, UserId = invitee.Id, InvitedById = member.UserId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Problem($"{invitee.UserName} is already invited.", StatusCodes.Status409Conflict);
        }

        return Results.Created($"/api/projects/{project.Label}/invitations/{invitee.UserName}", await ToResponseAsync(db, project, member.Role, ct));
    }

    private static async Task<IResult> CancelInvitationAsync(
        string label, string userName, ClaimsPrincipal user, JorvisDatabaseContext db, UserManager<AppUser> users,
        CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, _))
            return Results.NotFound();
        if (await users.FindByNameAsync(userName) is not { } invitee)
            return Results.NotFound();

        var deleted = await db.ProjectInvitations
            .Where(i => i.ProjectId == project.Id && i.UserId == invitee.Id)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>The owner can remove anyone else; anyone else can remove themselves (leave).</summary>
    private static async Task<IResult> RemoveMemberAsync(
        string label, string userName, ClaimsPrincipal user, JorvisDatabaseContext db, UserManager<AppUser> users,
        CancellationToken ct)
    {
        if (await FindMembershipAsync(db, label, user, ct) is not (var project, var member))
            return Results.NotFound();
        if (await users.FindByNameAsync(userName) is not { } target)
            return Results.NotFound();

        var leaving = target.Id == member.UserId;
        if (leaving && member.Role == ProjectRole.Owner)
            return Problem("The owner can't leave their project. Delete it instead.", StatusCodes.Status409Conflict);
        if (!leaving && member.Role != ProjectRole.Owner)
            return Problem(NotOwner, StatusCodes.Status403Forbidden);

        var deleted = await db.ProjectMembers
            .Where(m => m.ProjectId == project.Id && m.UserId == target.Id)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ListInvitationsAsync(ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        var userId = user.GetUserId();
        var invitations = await db.ProjectInvitations
            .Where(i => i.UserId == userId)
            .Join(db.Projects, i => i.ProjectId, p => p.Id, (i, p) => new { Invitation = i, Project = p })
            .Join(db.Users, x => x.Invitation.InvitedById, u => u.Id, (x, u) => new InvitationResponse(
                x.Project.Label, x.Project.Name, u.UserName!, x.Invitation.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(invitations.OrderByDescending(i => i.CreatedAt));
    }

    private static async Task<IResult> AcceptAsync(string label, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindInvitationAsync(db, label, user, ct) is not { } invitation)
            return Results.NotFound();

        db.ProjectInvitations.Remove(invitation);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = invitation.ProjectId, UserId = invitation.UserId, Role = ProjectRole.Member });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Accepted twice at once: the other request already made them a member.
            return Results.NotFound();
        }

        var project = await db.Projects.SingleAsync(p => p.Id == invitation.ProjectId, ct);
        return Results.Ok(await ToResponseAsync(db, project, ProjectRole.Member, ct));
    }

    private static async Task<IResult> DeclineAsync(string label, ClaimsPrincipal user, JorvisDatabaseContext db, CancellationToken ct)
    {
        if (await FindInvitationAsync(db, label, user, ct) is not { } invitation)
            return Results.NotFound();

        db.ProjectInvitations.Remove(invitation);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<ProjectInvitation?> FindInvitationAsync(
        JorvisDatabaseContext db, string label, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!ProjectLabels.TryNormalize(label, out label))
            return null;

        var userId = user.GetUserId();
        return await db.ProjectInvitations
            .Where(i => i.UserId == userId)
            .Join(db.Projects.Where(p => p.Label == label), i => i.ProjectId, p => p.Id, (i, _) => i)
            .SingleOrDefaultAsync(ct);
    }

    private static bool TryNormalizeName(string? value, out string name)
    {
        name = value?.Trim() ?? "";
        return name.Length is > 0 and <= JorvisDatabaseContext.MaxProjectNameLength;
    }

    private static async Task<ProjectResponse> ToResponseAsync(JorvisDatabaseContext db, Project project, ProjectRole role, CancellationToken ct)
    {
        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == project.Id)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new ProjectMemberResponse(u.UserName!, m.Role))
            .ToListAsync(ct);

        var invited = await db.ProjectInvitations
            .Where(i => i.ProjectId == project.Id)
            .Join(db.Users, i => i.UserId, u => u.Id, (_, u) => u.UserName!)
            .ToListAsync(ct);

        var lanes = await db.Lanes
            .Where(l => l.ProjectId == project.Id)
            .OrderBy(l => l.Position)
            .Select(l => new LaneResponse(l.Id, l.Name, l.Position))
            .ToListAsync(ct);

        return new ProjectResponse(
            project.Id,
            project.Label,
            project.Name,
            role,
            [.. members.OrderByDescending(m => m.Role).ThenBy(m => m.UserName, StringComparer.OrdinalIgnoreCase)],
            [.. invited.Order(StringComparer.OrdinalIgnoreCase)],
            lanes);
    }
}
