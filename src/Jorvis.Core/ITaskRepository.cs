namespace Jorvis.Core;

public interface ITaskRepository
{
    Task<List<TaskItem>> GetAllAsync(CancellationToken ct = default);
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TaskItem task, CancellationToken ct = default);
    void Remove(TaskItem task);
    Task SaveChangesAsync(CancellationToken ct = default);
}
