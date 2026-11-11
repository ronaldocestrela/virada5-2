using Application.UseCases.Rosters;
using Infrastructure.Data.Identity;

namespace WebApp.Blazor.Endpoints;

/// <summary>
/// Endpoints HTTP dedicados à exportação autenticada de arquivos da escala semanal.
/// </summary>
public static class RosterEndpoints
{
    public static IEndpointRouteBuilder MapRosterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/rosters/{rosterId:guid}/export", async (
            Guid rosterId,
            ExportRosterCsvUseCase exportUseCase,
            CancellationToken ct) =>
        {
            var result = await exportUseCase.ExecuteAsync(new ExportRosterCsvCommand(rosterId), ct);
            if (result.IsFailure)
            {
                return result.Error.Code is "Roster.NotFound" or "Company.NotFound"
                    ? Results.NotFound(new { error = result.Error.Code, message = result.Error.Message })
                    : Results.BadRequest(new { error = result.Error.Code, message = result.Error.Message });
            }

            return Results.File(
                result.Value.Content,
                result.Value.ContentType,
                result.Value.FileName);
        })
        .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Owner, ApplicationRoles.Manager));

        return endpoints;
    }
}

