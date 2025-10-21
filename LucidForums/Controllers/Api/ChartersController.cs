using LucidForums.Services.Charters;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public class ChartersController(ICharterService charterService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Models.Entities.Charter>>> GetAll(CancellationToken ct)
    {
        var items = await charterService.ListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Models.Entities.Charter>> GetById(Guid id, CancellationToken ct)
    {
        var item = await charterService.GetByIdAsync(id, ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    public record CharterRequest(string Name, string? Purpose, IEnumerable<string>? Rules, IEnumerable<string>? Behaviors);

    [HttpPost]
    public async Task<ActionResult<Models.Entities.Charter>> Create([FromBody] CharterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required");
        var created = await charterService.CreateAsync(request.Name, request.Purpose, request.Rules, request.Behaviors, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CharterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required");
        var ok = await charterService.UpdateAsync(id, request.Name, request.Purpose, request.Rules, request.Behaviors, ct);
        if (!ok) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await charterService.DeleteAsync(id, ct);
        if (!ok) return NotFound();
        return NoContent();
    }
}
