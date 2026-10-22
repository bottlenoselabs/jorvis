using Jorvis.Core;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jorvis.Infrastructure.Data;

public class JorvisDatabaseContext(DbContextOptions<JorvisDatabaseContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    public const int MaxProjectNameLength = 100;
    public const int MaxLaneNameLength = 50;
    public const int MaxTaskTitleLength = 200;
    public const int MaxTaskDescriptionLength = 10_000;

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();

    public DbSet<Lane> Lanes => Set<Lane>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Invite> Invites => Set<Invite>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // Dropping Identity's phone number columns (below) is deliberate, so don't warn about it on every start.
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.MappedPropertyIgnoredWarning));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity's defaults are prefixed "AspNet"; name its tables like ours instead.
        builder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            // Sign-in is by email, so phone numbers are never collected.
            entity.Ignore(u => u.PhoneNumber);
            entity.Ignore(u => u.PhoneNumberConfirmed);
            // Passkeys only: no password is ever stored, so there is no hash to steal.
            entity.Ignore(u => u.PasswordHash);
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityUserPasskey<Guid>>().ToTable("UserPasskeys");

        builder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Label).IsRequired().HasMaxLength(ProjectLabels.MaxLength);
            entity.HasIndex(p => p.Label).IsUnique();
            entity.Property(p => p.Name).IsRequired().HasMaxLength(MaxProjectNameLength);
            entity.Property(p => p.NextTaskNumber).IsConcurrencyToken();
        });

        // Removing a user removes their memberships and invitations; their projects stay for the other members.
        builder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(m => new { m.ProjectId, m.UserId });
            entity.HasOne<Project>().WithMany(p => p.Members).HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(m => m.UserId);
        });

        builder.Entity<ProjectInvitation>(entity =>
        {
            entity.HasKey(i => new { i.ProjectId, i.UserId });
            entity.HasOne<Project>().WithMany(p => p.Invitations).HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(i => i.InvitedById).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(i => i.UserId);
        });

        builder.Entity<Lane>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Name).IsRequired().HasMaxLength(MaxLaneNameLength);
            entity.HasOne<Project>().WithMany(p => p.Lanes).HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => new { t.ProjectId, t.Number }).IsUnique();
            entity.Property(t => t.Title).IsRequired().HasMaxLength(MaxTaskTitleLength);
            entity.Property(t => t.Description).HasMaxLength(MaxTaskDescriptionLength);
            entity.HasOne<Project>().WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            // A lane can only be deleted once it is empty, so never silently take its tasks with it.
            entity.HasOne<Lane>().WithMany().HasForeignKey(t => t.LaneId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Invite>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.TokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.Property(i => i.Email).HasMaxLength(256);
            entity.Property(i => i.UsedAt).IsConcurrencyToken();
        });
    }
}
