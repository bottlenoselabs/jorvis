using System.Net.Http.Json;
using Jorvis.Core.Contracts;

namespace Jorvis.Web.Services;

public class TaskApiClient(HttpClient http)
{
    public async Task<List<TaskResponse>> GetAllAsync() =>
        await http.GetFromJsonAsync<List<TaskResponse>>("api/tasks") ?? [];

    public async Task<TaskResponse?> CreateAsync(CreateTaskRequest request)
    {
        var response = await http.PostAsJsonAsync("api/tasks", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TaskResponse>();
    }

    public async Task UpdateAsync(Guid id, UpdateTaskRequest request)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id)
    {
        var response = await http.DeleteAsync($"api/tasks/{id}");
        response.EnsureSuccessStatusCode();
    }
}
