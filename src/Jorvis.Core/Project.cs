using System.Text.Json.Serialization;

namespace Jorvis.Core;

/// <summary>A shared board of tasks. Only its members can see it.</summary>
public class Project
{
    /// <summary>Generated when the project is created. Time-ordered (UUIDv7), so new rows go at the end of the index.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The unique kebab-case name used in URLs and the API, such as <c>home-reno</c>. See <see cref="ProjectLabels"/>.</summary>
    public required string Label { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The number the next task gets. Doubles as the concurrency token, so two new tasks never share one.</summary>
    public int NextTaskNumber { get; set; } = 1;

    public List<ProjectMember> Members { get; set; } = [];

    public List<ProjectInvitation> Invitations { get; set; } = [];

    public List<Lane> Lanes { get; set; } = [];

    public List<TaskItem> Tasks { get; set; } = [];
}

// Sent as "Owner" / "Member", which reads better than a number to anything else calling the API.
[JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))]
public enum ProjectRole
{
    Member = 0,

    /// <summary>Created the project: can also remove members and delete the project.</summary>
    Owner = 1
}

public class ProjectMember
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A member asked someone to join. They become a member only once they accept.</summary>
public class ProjectInvitation
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public Guid InvitedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A column on the board, such as "To do" or "Done".</summary>
public class Lane
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public required string Name { get; set; }

    /// <summary>Order on the board, left to right, starting at 0.</summary>
    public int Position { get; set; }

    public static readonly string[] Defaults = ["Backlog", "To do", "In progress", "Done"];
}
