using DaisiGit.Services;

namespace DaisiGit.Web.Endpoints;

/// <summary>
/// Daisinet-facing endpoints used by the workflow step editor.
/// Currently only exposes the model list for the run-minion step's dropdown.
/// </summary>
public static class DaisinetApiEndpoints
{
    public static void MapDaisinetApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/git/daisinet").RequireAuthorization();

        api.MapGet("/models", ListModels);
    }

    private static async Task<IResult> ListModels(
        DaisinetModelCatalog catalog, CancellationToken ct)
    {
        try
        {
            var models = await catalog.GetTextGenModelsAsync(ct);
            return Results.Ok(models);
        }
        catch (InvalidOperationException ex)
        {
            // System secret missing — surface as 503 (feature unavailable until ops sets it up).
            return Results.Problem(ex.Message, statusCode: 503);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                $"Could not load models from ORC: {ex.Message}", statusCode: 502);
        }
    }
}
