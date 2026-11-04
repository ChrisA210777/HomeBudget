using System.Security.Claims;
using Budget.Infrastructure.Identity;

namespace Budget.Server.Identity;

/// <summary>Reads the signed-in user's id from the current request.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
