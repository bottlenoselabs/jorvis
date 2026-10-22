using System.Net;
using System.Net.Http.Json;
using Jorvis.Core.Contracts;

namespace Jorvis.Api.Tests;

/// <summary>The board: a project's lanes and the tasks in them.</summary>
public sealed class TaskEndpointsTests : IDisposable, IAsyncLifetime
{
    private const string Tasks = "/api/projects/home-reno/tasks";
    private const string Lanes = "/api/projects/home-reno/lanes";

    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;
    private TestAuthenticator? _authenticator;
    private List<LaneResponse> _lanes = [];

    public TaskEndpointsTests() => _client = _factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _authenticator = await _factory.SignInAsync(_client);
        var created = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Home Reno", "home-reno"), Ct);
        _lanes = (await created.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!.Lanes;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
        _authenticator?.Dispose();
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<TaskResponse> CreateAsync(string title, Guid? laneId = null)
    {
        var response = await _client.PostAsJsonAsync(Tasks, new CreateTaskRequest(title, null, laneId), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(Ct))!;
    }

    private async Task<List<TaskResponse>> LaneAsync(Guid laneId) =>
        [.. (await _client.GetFromJsonAsync<List<TaskResponse>>(Tasks, Ct))!.Where(t => t.LaneId == laneId).OrderBy(t => t.Position)];

    private async Task<HttpResponseMessage> MoveAsync(int number, Guid laneId, int position) =>
        await _client.PostAsJsonAsync($"{Tasks}/{number}/move", new MoveTaskRequest(laneId, position), Ct);

    [Fact]
    public async Task Create_ThenGet_ByNumber()
    {
        var task = await CreateAsync("Write tests");

        Assert.Equal(1, task.Number);
        Assert.Equal("home-reno-1", task.Key);
        Assert.Equal(_lanes[0].Id, task.LaneId);

        var fetched = await _client.GetFromJsonAsync<TaskResponse>($"{Tasks}/1", Ct);
        Assert.Equal("Write tests", fetched?.Title);
    }

    [Fact]
    public async Task Numbers_KeepCounting_AndAreNeverReused()
    {
        await CreateAsync("One");
        await CreateAsync("Two");
        await _client.DeleteAsync($"{Tasks}/2", Ct);

        Assert.Equal(3, (await CreateAsync("Three")).Number);
    }

    [Fact]
    public async Task Create_InALane_GoesToTheBottom()
    {
        var first = await CreateAsync("First", _lanes[2].Id);
        var second = await CreateAsync("Second", _lanes[2].Id);

        Assert.Equal((_lanes[2].Id, 0), (first.LaneId, first.Position));
        Assert.Equal((_lanes[2].Id, 1), (second.LaneId, second.Position));
    }

    [Fact]
    public async Task Create_InALaneFromAnotherProject_IsRejected()
    {
        var other = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest("Other", "other"), Ct);
        var otherLane = (await other.Content.ReadFromJsonAsync<ProjectResponse>(Ct))!.Lanes[0];

        var response = await _client.PostAsJsonAsync(Tasks, new CreateTaskRequest("Wrong board", null, otherLane.Id), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Create_WithBlankTitle_IsRejected(string? title)
    {
        var response = await _client.PostAsJsonAsync(Tasks, new CreateTaskRequest(title, null, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ThenDelete_Works()
    {
        var task = await CreateAsync("Old");

        var updated = await _client.PutAsJsonAsync($"{Tasks}/{task.Number}", new UpdateTaskRequest("New", "Some detail"), Ct);
        var body = await updated.Content.ReadFromJsonAsync<TaskResponse>(Ct);
        Assert.Equal(("New", "Some detail"), (body?.Title, body?.Description));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Tasks}/{task.Number}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Tasks}/{task.Number}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Move_ToAnotherLane_RenumbersBothLanes()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B");
        var c = await CreateAsync("C");
        var done = _lanes[^1].Id;
        var existing = await CreateAsync("Already done", done);

        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(b.Number, done, 0)).StatusCode);

        Assert.Equal([(a.Number, 0), (c.Number, 1)], (await LaneAsync(_lanes[0].Id)).Select(t => (t.Number, t.Position)));
        Assert.Equal([(b.Number, 0), (existing.Number, 1)], (await LaneAsync(done)).Select(t => (t.Number, t.Position)));
    }

    [Fact]
    public async Task Move_WithinALane_Reorders_AndClampsThePosition()
    {
        var a = await CreateAsync("A");
        var b = await CreateAsync("B");
        var c = await CreateAsync("C");

        await MoveAsync(a.Number, _lanes[0].Id, 99);

        Assert.Equal([b.Number, c.Number, a.Number], (await LaneAsync(_lanes[0].Id)).Select(t => t.Number));
    }

    [Fact]
    public async Task Lanes_CanBeAddedRenamedAndReordered()
    {
        var created = await _client.PostAsJsonAsync(Lanes, new CreateLaneRequest("Review"), Ct);
        var review = (await created.Content.ReadFromJsonAsync<LaneResponse>(Ct))!;
        Assert.Equal(_lanes.Count, review.Position);

        await _client.PutAsJsonAsync($"{Lanes}/{review.Id}", new UpdateLaneRequest("Code review", 0), Ct);

        var project = await _client.GetFromJsonAsync<ProjectResponse>("/api/projects/home-reno", Ct);
        Assert.Equal(["Code review", .. _lanes.Select(l => l.Name)], project!.Lanes.Select(l => l.Name));
        Assert.Equal(Enumerable.Range(0, _lanes.Count + 1), project.Lanes.Select(l => l.Position));
    }

    [Fact]
    public async Task Lane_WithTasks_CannotBeDeleted_UntilItIsEmpty()
    {
        var task = await CreateAsync("Stuck", _lanes[1].Id);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"{Lanes}/{_lanes[1].Id}", Ct)).StatusCode);

        await MoveAsync(task.Number, _lanes[0].Id, 0);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{Lanes}/{_lanes[1].Id}", Ct)).StatusCode);

        var project = await _client.GetFromJsonAsync<ProjectResponse>("/api/projects/home-reno", Ct);
        Assert.Equal([0, 1, 2], project!.Lanes.Select(l => l.Position));
    }

    [Fact]
    public async Task TheLastLane_CannotBeDeleted()
    {
        foreach (var lane in _lanes[1..])
            await _client.DeleteAsync($"{Lanes}/{lane.Id}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"{Lanes}/{_lanes[0].Id}", Ct)).StatusCode);
    }
}
