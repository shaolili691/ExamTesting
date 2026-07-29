using System.Security.Claims;
using TaeExam.Api.Models;

namespace TaeExam.Api.Authorization;

public static class CurrentUser
{
    public static int Id(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(RoleNames.Administrator);
}
