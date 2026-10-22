using System.Net;
using System.Net.Http.Json;
using Jorvis.Core;
using Jorvis.Core.Contracts;
using Jorvis.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Jorvis.Api.Tests;

public sealed class ProjectEndpointsTests : IDisposable, IAsyncLifetime
{
    private readonly ApiFactory _factory = new();
    private readonly List<IDisposable> _disposables = [];

    // Two people, each in their own browser.
    private HttpClient _owner = null!;
    private HttpClient _friend = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _owner = await NewUserAsync("owner@example.com", "owner");
        _friend = await NewUserAsync("friend@example.com", "friend");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpClient> NewUserAsync(string email, string username)
    {
        var client = _factory.CreateClient();
        _disposables.Add(client);
        _disposables.Add(await _factory.SignInAsync(client, email, username));
        return client;
    }

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client, string label = "home-reno", string name = "Home Reno")
    {
        var response = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest(name, label), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!;
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient client, string username, string label = "home-reno") =>
        client.PostAsJsonAsync($"/api/projects/{label}/invitations", new InviteRequest(username), Ct);

    private async Task JoinAsync(HttpClient client, string username, string label = "home-reno")
    {
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(_owner, username, label)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/invitations/{label}/accept", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_MakesTheCreatorOwner_WithDefaultLanes()
    {
        var project = await CreateProjectAsync(_owner);

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("home-reno", project.Label);
        Assert.Equal("Home Reno", project.Name);
        Assert.Equal(ProjectRole.Owner, project.Role);
        Assert.Equal(new ProjectMemberResponse("owner", ProjectRole.Owner), Assert.Single(project.Members));
        Assert.Equal(Lane.Defaults, project.Lanes.Select(l => l.Name));

        var listed = Assert.Single((await _owner.GetFromJsonAsync<List<ProjectSummaryResponse>>("/api/projects", Ct))!);
        Assert.Equal(new ProjectSummaryResponse(project.Id, "home-reno", "Home Reno", ProjectRole.Owner, 1), listed);
    }

    [Fact]
    public async Task Create_NormalizesTheLabelToLowercase()
    {
        var project = await CreateProjectAsync(_owner, label: " Home-Reno ");

        Assert.Equal("home-reno", project.Label);
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync("/api/projects/HOME-RENO", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("home reno")]
    [InlineData("home_reno")]
    [InlineData("-home")]
    [InlineData("home-")]
    [InlineData("home--reno")]
    [InlineData("this-label-is-far-too-long-to-be-accepted-ok")]
    public async Task Create_WithAnInvalidLabel_IsRejected(string? label)
    {
        var response = await _owner.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Home Reno", label), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutAName_IsRejected()
    {
        var response = await _owner.PostAsJsonAsync("/api/projects", new CreateProjectRequest(" ", "home-reno"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithATakenLabel_IsRejected_EvenIfSomeoneElseOwnsIt()
    {
        await CreateProjectAsync(_owner);

        var response = await _friend.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Mine", "home-reno"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ProjectLabels.Taken, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Projects_AreInvisibleToNonMembers()
    {
        await CreateProjectAsync(_owner);

        Assert.Empty((await _friend.GetFromJsonAsync<List<ProjectSummaryResponse>>("/api/projects", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.GetAsync("/api/projects/home-reno", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.GetAsync("/api/projects/home-reno/tasks", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await InviteAsync(_friend, "friend")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.PostAsJsonAsync(
            "/api/projects/home-reno/tasks", new CreateTaskRequest("Sneaky", null, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.DeleteAsync("/api/projects/home-reno", Ct)).StatusCode);
    }

    [Fact]
    public async Task Invite_ThenAccept_JoinsTheProject()
    {
        await CreateProjectAsync(_owner);

        var invited = await InviteAsync(_owner, "Friend");

        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        Assert.Equal(["friend"], (await invited.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!.InvitedUserNames);
        // Being invited is not being a member yet.
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.GetAsync("/api/projects/home-reno", Ct)).StatusCode);

        var invitation = Assert.Single((await _friend.GetFromJsonAsync<List<InvitationResponse>>("/api/invitations", Ct))!);
        Assert.Equal(("home-reno", "Home Reno", "owner"), (invitation.ProjectLabel, invitation.ProjectName, invitation.InvitedBy));

        var accepted = await _friend.PostAsync("/api/invitations/home-reno/accept", null, Ct);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var project = await _friend.GetFromJsonAsync<ProjectResponse>("/api/projects/home-reno", Ct);
        Assert.Equal(ProjectRole.Member, project!.Role);
        Assert.Equal(["owner", "friend"], project.Members.Select(m => m.UserName));
        Assert.Empty(project.InvitedUserNames);
        Assert.Empty((await _friend.GetFromJsonAsync<List<InvitationResponse>>("/api/invitations", Ct))!);
    }

    [Fact]
    public async Task Invite_AnUnknownUsername_IsNotFound()
    {
        await CreateProjectAsync(_owner);

        Assert.Equal(HttpStatusCode.NotFound, (await InviteAsync(_owner, "nobody")).StatusCode);
    }

    [Fact]
    public async Task Invite_SomeoneAlreadyInvitedOrAMember_IsAConflict()
    {
        await CreateProjectAsync(_owner);

        Assert.Equal(HttpStatusCode.Conflict, (await InviteAsync(_owner, "owner")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(_owner, "friend")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await InviteAsync(_owner, "friend")).StatusCode);
    }

    [Fact]
    public async Task Members_CanInviteOthersToo()
    {
        await CreateProjectAsync(_owner);
        await JoinAsync(_friend, "friend");
        var third = await NewUserAsync("third@example.com", "third");

        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(_friend, "third")).StatusCode);
        Assert.Equal("friend", Assert.Single((await third.GetFromJsonAsync<List<InvitationResponse>>("/api/invitations", Ct))!).InvitedBy);
    }

    [Fact]
    public async Task Decline_RemovesTheInvitation()
    {
        await CreateProjectAsync(_owner);
        await InviteAsync(_owner, "friend");

        Assert.Equal(HttpStatusCode.NoContent, (await _friend.DeleteAsync("/api/invitations/home-reno", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _friend.PostAsync("/api/invitations/home-reno/accept", null, Ct)).StatusCode);
        Assert.Empty((await _owner.GetFromJsonAsync<ProjectResponse>("/api/projects/home-reno", Ct))!.InvitedUserNames);
    }

    [Fact]
    public async Task CancelledInvitation_CannotBeAccepted()
    {
        await CreateProjectAsync(_owner);
        await InviteAsync(_owner, "friend");

        Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync("/api/projects/home-reno/invitations/friend", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _friend.PostAsync("/api/invitations/home-reno/accept", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Accept_WithoutAnInvitation_IsNotFound()
    {
        await CreateProjectAsync(_owner);

        Assert.Equal(HttpStatusCode.NotFound, (await _friend.PostAsync("/api/invitations/home-reno/accept", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Members_CanLeave_ButTheOwnerCannot()
    {
        await CreateProjectAsync(_owner);
        await JoinAsync(_friend, "friend");

        Assert.Equal(HttpStatusCode.Conflict, (await _owner.DeleteAsync("/api/projects/home-reno/members/owner", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _friend.DeleteAsync("/api/projects/home-reno/members/friend", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.GetAsync("/api/projects/home-reno", Ct)).StatusCode);
    }

    [Fact]
    public async Task OnlyTheOwner_CanRemoveOthers_RenameOrDelete()
    {
        await CreateProjectAsync(_owner);
        await JoinAsync(_friend, "friend");

        Assert.Equal(HttpStatusCode.Forbidden, (await _friend.DeleteAsync("/api/projects/home-reno/members/owner", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _friend.PutAsJsonAsync("/api/projects/home-reno", new RenameProjectRequest("Mine"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _friend.DeleteAsync("/api/projects/home-reno", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsJsonAsync("/api/projects/home-reno", new RenameProjectRequest("House"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync("/api/projects/home-reno/members/friend", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _friend.GetAsync("/api/projects/home-reno", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheProjectAndItsTasks_AndFreesTheLabel()
    {
        var first = await CreateProjectAsync(_owner);
        await _owner.PostAsJsonAsync("/api/projects/home-reno/tasks", new CreateTaskRequest("Paint", null, null), Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync("/api/projects/home-reno", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _owner.GetAsync("/api/projects/home-reno", Ct)).StatusCode);
        var again = await CreateProjectAsync(_friend);
        Assert.Equal("home-reno", again.Label);
        // Reusing the label still makes a new project.
        Assert.NotEqual(first.Id, again.Id);
        Assert.Empty((await _friend.GetFromJsonAsync<List<TaskResponse>>("/api/projects/home-reno/tasks", Ct))!);
    }

    [Fact]
    public async Task RemovingAUser_RemovesTheirMembership()
    {
        await CreateProjectAsync(_owner);
        await JoinAsync(_friend, "friend");

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByNameAsync("friend"))!)).Succeeded);
        }

        var project = await _owner.GetFromJsonAsync<ProjectResponse>("/api/projects/home-reno", Ct);
        Assert.Equal(["owner"], project!.Members.Select(m => m.UserName));
    }
}
