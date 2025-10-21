using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Charters;

public class CharterService(ApplicationDbContext db) : ICharterService
{
    public async Task<List<Models.Entities.Charter>> ListAsync(CancellationToken ct = default)
    {
        return await db.Charters.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public Task<Models.Entities.Charter?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return db.Charters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Models.Entities.Charter> CreateAsync(string name, string? purpose, IEnumerable<string>? rules, IEnumerable<string>? behaviors, CancellationToken ct = default)
    {
        var charter = new Models.Entities.Charter
        {
            Name = name,
            Purpose = purpose,
            Rules = rules?.ToList() ?? new List<string>(),
            Behaviors = behaviors?.ToList() ?? new List<string>()
        };
        db.Charters.Add(charter);
        await db.SaveChangesAsync(ct);
        return charter;
    }

    public async Task<bool> UpdateAsync(Guid id, string name, string? purpose, IEnumerable<string>? rules, IEnumerable<string>? behaviors, CancellationToken ct = default)
    {
        var charter = await db.Charters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter is null) return false;
        charter.Name = name;
        charter.Purpose = purpose;
        charter.Rules = rules?.ToList() ?? new List<string>();
        charter.Behaviors = behaviors?.ToList() ?? new List<string>();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var charter = await db.Charters.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (charter is null) return false;
        db.Charters.Remove(charter);
        await db.SaveChangesAsync(ct);
        return true;
    }
}