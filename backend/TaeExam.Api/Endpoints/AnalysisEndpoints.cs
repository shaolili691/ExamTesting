using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this WebApplication app)
    {
        app.MapGet("/api/analysis/overview", async (AnalysisService svc) =>
        {
            var overview = await svc.GetOverviewAsync();
            return Results.Ok(overview);
        });
    }
}
