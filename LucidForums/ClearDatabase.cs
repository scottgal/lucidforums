using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums;

public static class DatabaseClearer
{
    public static async Task ClearAllForumsAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM ""ContentTranslations"";
            DELETE FROM ""TranslationQueue"";
            DELETE FROM ""Messages"";
            DELETE FROM ""Threads"";
            DELETE FROM ""ForumUsers"";
            DELETE FROM ""Forums"";
            DELETE FROM ""Charters"";
        ");

        Console.WriteLine("✓ All forums, threads, messages, and translations have been deleted.");
    }
}
