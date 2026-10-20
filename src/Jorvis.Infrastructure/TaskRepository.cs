using Jorvis.Core;
using Microsoft.EntityFrameworkCore;

namespace Jorvis.Infrastructure;

public class TaskRepository(AppDbContext db) : ITaskRepository
{
    public async Task<List<TaskItem>> GetAllAsync(CancellationToken ct = default) =>
        await db.Tasks.OrderBy(t => t.IsDone).ThenBy(t => t.DueDate).ToListAsync(ct);

    public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Tasks.FindAsync([id], ct);

    public async Task AddAsync(TaskItem task, CancellationToken ct = default) =>
        await db.Tasks.AddAsync(task, ct);

    public void Remove(TaskItem task) => db.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
