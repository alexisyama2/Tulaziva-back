using System.Security.Claims;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Reports;

namespace Tulaziva.Api.Endpoints;

public sealed record ReportRequest(string Reason, string? Details = null);

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ads/{id:guid}/report", async (Guid id, ReportRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new ReportAdCommand(principal.GetUserId(), id, request.Reason, request.Details), cancellationToken);
                return Results.NoContent();
            })
            .WithTags("Reports")
            .WithSummary("Reports a public ad of another user (spam, fraud, inappropriate, prohibited or other). Once per user and ad.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var admin = app.MapGroup("/api/admin/reports").WithTags("Admin").RequireAuthorization(AdminEndpoints.AdminPolicy);

        admin.MapGet("/", async (IDispatcher dispatcher, CancellationToken cancellationToken, string? status, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetReportsQuery(status, page, pageSize), cancellationToken)))
            .WithSummary("The reports of the ads. Filter by status (open, resolved, dismissed). Open ones come oldest first.")
            .Produces<PagedResult<ReportDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapPost("/{id:guid}/resolve", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new ResolveReportCommand(principal.GetUserId(), id), cancellationToken)))
            .WithSummary("Takes the ad down and closes every open report about it as resolved.")
            .Produces<ReportDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPost("/{id:guid}/dismiss", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new DismissReportCommand(principal.GetUserId(), id), cancellationToken)))
            .WithSummary("Closes the report and leaves the ad as it was.")
            .Produces<ReportDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
