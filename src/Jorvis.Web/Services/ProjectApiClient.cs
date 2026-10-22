using System.Net.Http.Json;
using Jorvis.Core.Contracts;

namespace Jorvis.Web.Services;

/// <summary>A request the API refused, carrying its explanation (the problem's detail) to show the person.</summary>
public class ApiException(string message) : Exception(message);

public class ProjectApiClient(HttpClient http)
{
    public event Action? ProjectsChanged;

    public async Task<List<ProjectSummaryResponse>> GetProjectsAsync() =>
        await http.GetFromJsonAsync<List<ProjectSummaryResponse>>("api/projects") ?? [];

    /// <summary>Null when the project does not exist or the signed-in user is not a member.</summary>
    public async Task<ProjectResponse?> GetProjectAsync(string label)
    {
        var response = await http.GetAsync($"api/projects/{Uri.EscapeDataString(label)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        return await ReadAsync<ProjectResponse>(response);
    }

    public async Task<ProjectResponse> CreateProjectAsync(CreateProjectRequest request) =>
        Changed(await ReadAsync<ProjectResponse>(await http.PostAsJsonAsync("api/projects", request)));

    public async Task<ProjectResponse> RenameProjectAsync(string label, string name) =>
        Changed(await ReadAsync<ProjectResponse>(await http.PutAsJsonAsync(Project(label), new RenameProjectRequest(name))));

    public async Task DeleteProjectAsync(string label)
    {
        await EnsureSuccessAsync(await http.DeleteAsync(Project(label)));
        ProjectsChanged?.Invoke();
    }

    public async Task<ProjectResponse> InviteAsync(string label, string userName) =>
        await ReadAsync<ProjectResponse>(await http.PostAsJsonAsync($"{Project(label)}/invitations", new InviteRequest(userName)));

    public async Task CancelInvitationAsync(string label, string userName) =>
        await EnsureSuccessAsync(await http.DeleteAsync($"{Project(label)}/invitations/{Uri.EscapeDataString(userName)}"));

    /// <summary>Removes someone else (owner only) or, with your own username, leaves the project.</summary>
    public async Task RemoveMemberAsync(string label, string userName)
    {
        await EnsureSuccessAsync(await http.DeleteAsync($"{Project(label)}/members/{Uri.EscapeDataString(userName)}"));
        ProjectsChanged?.Invoke();
    }

    public async Task<List<InvitationResponse>> GetInvitationsAsync() =>
        await http.GetFromJsonAsync<List<InvitationResponse>>("api/invitations") ?? [];

    public async Task<ProjectResponse> AcceptInvitationAsync(string label) =>
        Changed(await ReadAsync<ProjectResponse>(await http.PostAsync($"api/invitations/{Uri.EscapeDataString(label)}/accept", null)));

    public async Task DeclineInvitationAsync(string label) =>
        await EnsureSuccessAsync(await http.DeleteAsync($"api/invitations/{Uri.EscapeDataString(label)}"));

    public async Task<LaneResponse> CreateLaneAsync(string label, string name) =>
        await ReadAsync<LaneResponse>(await http.PostAsJsonAsync($"{Project(label)}/lanes", new CreateLaneRequest(name)));

    public async Task<LaneResponse> UpdateLaneAsync(string label, Guid id, UpdateLaneRequest request) =>
        await ReadAsync<LaneResponse>(await http.PutAsJsonAsync($"{Project(label)}/lanes/{id}", request));

    public async Task DeleteLaneAsync(string label, Guid id) =>
        await EnsureSuccessAsync(await http.DeleteAsync($"{Project(label)}/lanes/{id}"));

    public async Task<List<TaskResponse>> GetTasksAsync(string label) =>
        await http.GetFromJsonAsync<List<TaskResponse>>($"{Project(label)}/tasks") ?? [];

    public async Task<TaskResponse> CreateTaskAsync(string label, CreateTaskRequest request) =>
        await ReadAsync<TaskResponse>(await http.PostAsJsonAsync($"{Project(label)}/tasks", request));

    public async Task<TaskResponse> UpdateTaskAsync(string label, int number, UpdateTaskRequest request) =>
        await ReadAsync<TaskResponse>(await http.PutAsJsonAsync($"{Project(label)}/tasks/{number}", request));

    public async Task<TaskResponse> MoveTaskAsync(string label, int number, MoveTaskRequest request) =>
        await ReadAsync<TaskResponse>(await http.PostAsJsonAsync($"{Project(label)}/tasks/{number}/move", request));

    public async Task DeleteTaskAsync(string label, int number) =>
        await EnsureSuccessAsync(await http.DeleteAsync($"{Project(label)}/tasks/{number}"));

    private static string Project(string label) => $"api/projects/{Uri.EscapeDataString(label)}";

    /// <summary>Tells the sidebar to reload its list of projects.</summary>
    private T Changed<T>(T result)
    {
        ProjectsChanged?.Invoke();
        return result;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        Problem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<Problem>();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Not a problem response (an empty 404, or a proxy error page).
        }

        throw new ApiException(problem?.Detail ?? problem?.Title ?? $"The request failed ({(int)response.StatusCode}).");
    }

    /// <summary>The parts of an RFC 9457 problem response worth showing.</summary>
    private sealed record Problem(string? Title, string? Detail);
}
