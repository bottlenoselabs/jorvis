namespace Jorvis.Core.Contracts;

/// <summary><see cref="Id"/> is assigned when the project is created and never changes.</summary>
public record ProjectSummaryResponse(Guid Id, string Label, string Name, ProjectRole Role, int MemberCount);

/// <summary><see cref="Id"/> is assigned when the project is created and never changes.</summary>
public record ProjectResponse(
    Guid Id,
    string Label,
    string Name,
    ProjectRole Role,
    List<ProjectMemberResponse> Members,
    List<string> InvitedUserNames,
    List<LaneResponse> Lanes);

public record ProjectMemberResponse(string UserName, ProjectRole Role);

public record CreateProjectRequest(string? Name, string? Label);

public record RenameProjectRequest(string? Name);

public record InviteRequest(string? UserName);

/// <summary>An invitation to someone else's project, as the invited person sees it.</summary>
public record InvitationResponse(string ProjectLabel, string ProjectName, string InvitedBy, DateTimeOffset CreatedAt);

public record LaneResponse(Guid Id, string Name, int Position);

public record CreateLaneRequest(string? Name);

/// <summary>Renames and/or moves a lane. Position is clamped to the board.</summary>
public record UpdateLaneRequest(string? Name, int? Position);

/// <summary><see cref="Key"/> is the task's natural key, <c>{label}-{number}</c>.</summary>
public record TaskResponse(
    int Number,
    string Key,
    Guid LaneId,
    int Position,
    string Title,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Without a lane, the task goes to the bottom of the first lane.</summary>
public record CreateTaskRequest(string? Title, string? Description, Guid? LaneId);

public record UpdateTaskRequest(string? Title, string? Description);

/// <summary>Moves a task to a lane (possibly its own) at a position, which is clamped to the lane.</summary>
public record MoveTaskRequest(Guid LaneId, int Position);
