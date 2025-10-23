using LucidForums.Helpers;

namespace LucidForums.Models.Configuration;

public class JwtOptions : IConfigSection
{
    public static string Section => "Jwt";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "LucidForums";
    public string Audience { get; set; } = "LucidForumsAPI";
    public int ExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
