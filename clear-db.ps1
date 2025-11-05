# Clear the database using SQL
$connString = "Host=localhost;Port=5432;Database=lucidforums;Username=postgres"

# Using Npgsql from .NET
Add-Type -Path "C:\Users\scott\.nuget\packages\npgsql\9.0.4\lib\net9.0\Npgsql.dll"

$conn = [Npgsql.NpgsqlConnection]::new($connString)
$conn.Open()

Write-Host "Clearing all content..."

$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
    DELETE FROM ""ContentTranslations"";
    DELETE FROM ""TranslationQueue"";
    DELETE FROM ""Messages"";
    DELETE FROM ""Threads"";
    DELETE FROM ""ForumUsers"";
    DELETE FROM ""Forums"";
    DELETE FROM message_embeddings;
"@
$cmd.ExecuteNonQuery() | Out-Null

$conn.Close()

Write-Host "✓ All content has been cleared successfully."
Write-Host "Restart the application to trigger auto-seeding."
