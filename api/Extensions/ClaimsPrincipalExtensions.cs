using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ApiInsightStudio.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>JWT içindeki kullanıcı kimliğini (sub veya NameIdentifier) okur.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal user, out int userId)
    {
        var claim = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out userId);
    }
}
