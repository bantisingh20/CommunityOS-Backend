namespace App.Application.MyTasks;

public interface IMyTasksService
{
    Task<IReadOnlyList<MyTaskSummaryItem>> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyTaskCard>> GetCardsAsync(string? type, CancellationToken cancellationToken = default);
}
