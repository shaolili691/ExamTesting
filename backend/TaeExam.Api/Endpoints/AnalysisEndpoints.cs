using TaeExam.Api.Authorization;
using TaeExam.Api.Services;

namespace TaeExam.Api.Endpoints;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this WebApplication app)
    {
        app.MapGet("/api/analysis/overview", async (AnalysisService svc, HttpContext http, string? scope = null, int? userId = null) =>
        {
            var callerId = CurrentUser.Id(http.User);
            var isAdmin = CurrentUser.IsAdmin(http.User);

            if (!isAdmin && (scope == "all" || userId.HasValue))
            {
                return Results.Json(new { error = "Missing permission: analysis:all" }, statusCode: StatusCodes.Status403Forbidden);
            }

            int? effectiveUserId = scope == "all" ? null : (userId ?? callerId);
            var overview = await svc.GetOverviewAsync(effectiveUserId);
            return Results.Ok(overview);
        }).RequirePermission("analysis:view");
    }
}
