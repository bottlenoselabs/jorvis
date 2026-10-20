using System.Net;
using System.Net.Http.Json;
using Jorvis.Core.Contracts;

namespace Jorvis.Api.Tests;

public sealed class TaskEndpointsTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public TaskEndpointsTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsCreatedTask()
    {
        var ct = TestContext.Current.CancellationToken;

        var created = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Write tests", null, null), ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Content.ReadFromJsonAsync<TaskResponse>(ct);
        Assert.NotNull(task);

        var fetched = await _client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}", ct);
        Assert.Equal("Write tests", fetched?.Title);
    }

    [Fact]
    public async Task Create_WithBlankTitle_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/tasks", new CreateTaskRequest(" ", null, null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ThenDelete_Works()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Old", null, null), ct);
        var task = await created.Content.ReadFromJsonAsync<TaskResponse>(ct);
        Assert.NotNull(task);

        var updated = await _client.PutAsJsonAsync(
            $"/api/tasks/{task.Id}", new UpdateTaskRequest("New", "notes", null, true), ct);
        var body = await updated.Content.ReadFromJsonAsync<TaskResponse>(ct);
        Assert.Equal("New", body?.Title);
        Assert.True(body?.IsDone);

        var deleted = await _client.DeleteAsync($"/api/tasks/{task.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await _client.GetAsync($"/api/tasks/{task.Id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
