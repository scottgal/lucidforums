using LucidForums.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Setup;

public class SetupService : ISetupService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        UserManager<User> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<SetupService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<bool> RequiresSetupAsync(CancellationToken ct = default)
    {
        // Check if any admin users exist
        var adminRole = await _roleManager.FindByNameAsync("Admin");
        if (adminRole == null)
        {
            // No Admin role exists, setup is required
            return true;
        }

        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        return adminUsers.Count == 0;
    }

    public async Task<bool> CreateAdminAsync(string email, string username, string password, CancellationToken ct = default)
    {
        try
        {
            // Ensure Admin role exists
            var adminRole = await _roleManager.FindByNameAsync("Admin");
            if (adminRole == null)
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole("Admin"));
                if (!roleResult.Succeeded)
                {
                    _logger.LogError("Failed to create Admin role: {Errors}", string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                    return false;
                }
            }

            // Create admin user
            var user = new User
            {
                UserName = username,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                _logger.LogError("Failed to create admin user: {Errors}", string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return false;
            }

            // Add to Admin role
            var addRoleResult = await _userManager.AddToRoleAsync(user, "Admin");
            if (!addRoleResult.Succeeded)
            {
                _logger.LogError("Failed to add user to Admin role: {Errors}", string.Join(", ", addRoleResult.Errors.Select(e => e.Description)));
                return false;
            }

            _logger.LogInformation("Successfully created admin user: {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating admin user");
            return false;
        }
    }
}
