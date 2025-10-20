using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LucidForums.Areas.Identity.Pages.Account.Manage;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}