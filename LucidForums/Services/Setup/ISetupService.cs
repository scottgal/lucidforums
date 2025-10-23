namespace LucidForums.Services.Setup;

/// <summary>
/// Service to detect and manage first-run setup state
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Checks if the application requires initial setup (no admin users exist)
    /// </summary>
    Task<bool> RequiresSetupAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates the initial administrator account
    /// </summary>
    Task<bool> CreateAdminAsync(string email, string username, string password, CancellationToken ct = default);
}
