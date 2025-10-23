using LucidForums.Models.ViewModels;
using LucidForums.Services.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

/// <summary>
/// Controller for first-run initial setup wizard (admin account creation)
/// </summary>
[AllowAnonymous]
[Route("initial-setup")]
public class InitialSetupController : Controller
{
    private readonly ISetupService _setupService;
    private readonly ISiteSetupService _siteSetupService;
    private readonly ILogger<InitialSetupController> _logger;

    public InitialSetupController(
        ISetupService setupService,
        ISiteSetupService siteSetupService,
        ILogger<InitialSetupController> logger)
    {
        _setupService = setupService;
        _siteSetupService = siteSetupService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Check if setup is required
        var requiresSetup = await _setupService.RequiresSetupAsync(ct);
        if (!requiresSetup)
        {
            // Setup already completed, redirect to home
            return RedirectToAction("Index", "Home");
        }

        // Redirect to complete setup page
        return RedirectToAction("Complete");
    }

    [HttpGet("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        // Check if setup is required
        var requiresSetup = await _setupService.RequiresSetupAsync(ct);
        if (!requiresSetup)
        {
            return RedirectToAction("Index", "Home");
        }

        return View();
    }

    [HttpPost("create-admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAdmin([FromForm] SetupAdminRequest request, CancellationToken ct)
    {
        // Verify setup is still required
        var requiresSetup = await _setupService.RequiresSetupAsync(ct);
        if (!requiresSetup)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", request);
        }

        var success = await _setupService.CreateAdminAsync(request.Email, request.Username, request.Password, ct);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, "Failed to create administrator account. Please check the logs for details.");
            return View("Index", request);
        }

        _logger.LogInformation("Initial administrator account created successfully");

        // Redirect to site setup page
        TempData["AdminCreated"] = $"Administrator account '{request.Username}' created successfully!";
        return RedirectToAction("SiteSetup");
    }

    [HttpGet("site-setup")]
    public IActionResult SiteSetup()
    {
        // Admin must be created first, but we'll allow access if any admin exists
        return View();
    }

    [HttpPost("generate-site")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateSite(
        [FromForm] int forumCount = 12,
        [FromForm] int usersPerForum = 5,
        [FromForm] int threadsPerForum = 10,
        [FromForm] int repliesPerThread = 5,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _siteSetupService.GenerateSiteAsync(
                forumCount,
                usersPerForum,
                threadsPerForum,
                repliesPerThread,
                ct);

            if (result.Success)
            {
                TempData["SetupComplete"] = $"Site setup completed! Created {result.ForumsCreated} forums, {result.UsersCreated} users, {result.ThreadsCreated} threads, and {result.MessagesCreated} messages.";
                return RedirectToAction("Index", "Home");
            }
            else
            {
                TempData["SetupError"] = string.Join(", ", result.Errors);
                return RedirectToAction("SiteSetup");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Site generation failed");
            TempData["SetupError"] = $"Site generation failed: {ex.Message}";
            return RedirectToAction("SiteSetup");
        }
    }

    [HttpPost("skip-site-setup")]
    [ValidateAntiForgeryToken]
    public IActionResult SkipSiteSetup()
    {
        TempData["SetupComplete"] = "Setup completed. You can start creating your own content!";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("complete-setup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteSetup([FromForm] SetupAdminRequest request, CancellationToken ct)
    {
        // Verify setup is still required
        var requiresSetup = await _setupService.RequiresSetupAsync(ct);
        if (!requiresSetup)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // Step 1: Create admin account
            var adminCreated = await _setupService.CreateAdminAsync(request.Email, request.Username, request.Password, ct);
            if (!adminCreated)
            {
                return BadRequest("Failed to create administrator account");
            }

            _logger.LogInformation("Administrator account created: {Username}", request.Username);

            // Step 2: Generate site content in background (SignalR will provide progress updates)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _siteSetupService.GenerateSiteAsync(
                        forumCount: 12,
                        usersPerForum: 5,
                        threadsPerForum: 10,
                        repliesPerThread: 5,
                        ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Site generation failed");
                }
            }, CancellationToken.None);

            return Ok(new { message = "Setup started successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Complete setup failed");
            return StatusCode(500, $"Setup failed: {ex.Message}");
        }
    }
}

