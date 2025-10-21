using LucidForums.Models.Entities;
using LucidForums.Models.ViewModels;
using LucidForums.Services.Charters;
using LucidForums.Web.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class CharterController(ICharterService charterService, IAppMapper mapper) : Controller
{
    [HttpGet]
    [Route("Charters")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await charterService.ListAsync(ct);
        var vms = mapper.ToCharterListItemVms(items);
        return View(vms);
    }

    [HttpGet]
    [Route("Charters/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var charter = await charterService.GetByIdAsync(id, ct);
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
        var rules = SplitLines(vm.RulesMultiline);
        var behaviors = SplitLines(vm.BehaviorsMultiline);
        var charter = await charterService.CreateAsync(vm.Name, vm.Purpose, rules, behaviors, ct);
        return RedirectToAction(nameof(Details), new { id = charter.Id });
    }

    [HttpGet]
    [Route("Charters/Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var charter = await charterService.GetByIdAsync(id, ct);
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
        var rules = SplitLines(vm.RulesMultiline);
        var behaviors = SplitLines(vm.BehaviorsMultiline);
        var ok = await charterService.UpdateAsync(id, vm.Name, vm.Purpose, rules, behaviors, ct);
        if (!ok) return NotFound();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Route("Charters/Delete/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var charter = await charterService.GetByIdAsync(id, ct);
        if (charter == null) return NotFound();
        var vm = mapper.ToCharterDetailsVm(charter);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Charters/Delete/{id:guid}")]
    public async Task<IActionResult> ConfirmDelete(Guid id, CancellationToken ct)
    {
        var ok = await charterService.DeleteAsync(id, ct);
        if (!ok) return NotFound();
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