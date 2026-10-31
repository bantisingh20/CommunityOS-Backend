using App.Application.MyTasks;
using App.Contracts.Envelope;

namespace App.Api.Endpoints;

public static class MyTasksEndpoints
{
    public static IEndpointRouteBuilder MapMyTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/my-tasks");

        group.MapGet("/summary", async (IMyTasksService svc, CancellationToken ct) =>
            {
                var items = await svc.GetSummaryAsync(ct);
                return Results.Ok(ApiResult<IReadOnlyList<MyTaskSummaryItem>>.Ok(items, string.Empty));
            })
            .RequireAuthorization();

        group.MapGet("/", async (string? type, IMyTasksService svc, CancellationToken ct) =>
            {
                var items = await svc.GetCardsAsync(type, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MyTaskCard>>.Ok(items, string.Empty));
            })
            .RequireAuthorization();

        return app;
    }
}
