using LucidForums.Data;
using LucidForums.Models.Entities;
using LucidForums.Models.ViewModels;
using LucidForums.Web.Mapping;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Controllers;

public class CharterController(ApplicationDbContext db, IAppMapper mapper) : Controller
{
    [HttpGet]
    [Route("Charters")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.Charters.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var vms = mapper.ToCharterListItemVms(items);
        return View(vms);
    }

    [HttpGet]
    [Route("Charters/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var charter = await db.Charters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter == null) return NotFound();
        var vm = mapper.ToCharterDetailsVm(charter);
        return View(vm);
    }

    [HttpGet]
    [Route("Charters/Create")]
    public IActionResult Create()
    {
        return View(new CharterEditVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Charters/Create")]
    public async Task<IActionResult> Create(CharterEditVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }
        var charter = new Charter
        {
            Name = vm.Name,
            Purpose = vm.Purpose,
            Rules = SplitLines(vm.RulesMultiline),
            Behaviors = SplitLines(vm.BehaviorsMultiline)
        };
        db.Charters.Add(charter);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = charter.Id });
    }

    [HttpGet]
    [Route("Charters/Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var charter = await db.Charters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter == null) return NotFound();
        var vm = new CharterEditVm
        {
            Id = charter.Id,
            Name = charter.Name,
            Purpose = charter.Purpose,
            RulesMultiline = string.Join("\n", charter.Rules ?? new()),
            BehaviorsMultiline = string.Join("\n", charter.Behaviors ?? new())
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Charters/Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CharterEditVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }
        var charter = await db.Charters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter == null) return NotFound();
        charter.Name = vm.Name;
        charter.Purpose = vm.Purpose;
        charter.Rules = SplitLines(vm.RulesMultiline);
        charter.Behaviors = SplitLines(vm.BehaviorsMultiline);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Route("Charters/Delete/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var charter = await db.Charters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter == null) return NotFound();
        var vm = mapper.ToCharterDetailsVm(charter);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Charters/Delete/{id:guid}")]
    public async Task<IActionResult> ConfirmDelete(Guid id, CancellationToken ct)
    {
        var charter = await db.Charters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter == null) return NotFound();
        db.Charters.Remove(charter);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    private static List<string> SplitLines(string text)
    {
        return (text ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}