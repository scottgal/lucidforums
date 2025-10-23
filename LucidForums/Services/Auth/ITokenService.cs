using LucidForums.Models.Entities;
using System.Security.Claims;

namespace LucidForums.Services.Auth;

public interface ITokenService
{
    string GenerateAccessToken(User user, IList<string> roles);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
