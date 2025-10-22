using System.IO.Hashing;
using System.Text;

namespace mostlylucid.llmtranslate.Helpers;

public static class ContentHash
{
    /// <summary>
    /// Generate a lowercase hex string of an xxHash64 of the provided content.
    /// Uses UTF-8 encoding for strings. By default takes the first 8 bytes (16 hex chars)
    /// which offers a good balance between collision resistance and DOM id length.
    /// </summary>
    /// <param name="content">Input string to hash. Null is treated as empty.</param>
    /// <param name="bytesToTake">Number of bytes from the 8-byte xxHash64 to return (1..8). Default is 8.</param>
    /// <returns>Lowercase hex string.</returns>
    public static string Generate(string? content, int bytesToTake = 8)
    {
        if (bytesToTake <= 0 || bytesToTake > 8)
            throw new ArgumentOutOfRangeException(nameof(bytesToTake), "bytesToTake must be between 1 and 8.");

        if (string.IsNullOrEmpty(content))
            content = string.Empty;

        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = XxHash64.Hash(bytes);
        return Convert.ToHexString(hash[..bytesToTake]).ToLowerInvariant();
    }
}
