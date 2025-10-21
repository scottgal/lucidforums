namespace LucidForums.Services.Charters;

public interface ICharterService
{
    Task<List<Models.Entities.Charter>> ListAsync(CancellationToken ct = default);
    Task<Models.Entities.Charter?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Models.Entities.Charter> CreateAsync(string name, string? purpose, IEnumerable<string>? rules, IEnumerable<string>? behaviors, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, string name, string? purpose, IEnumerable<string>? rules, IEnumerable<string>? behaviors, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}