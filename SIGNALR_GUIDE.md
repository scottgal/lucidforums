```markdown
# SignalR Integration Guide for LucidForums

## Overview
SignalR is used for real-time features across forums, translations, setup processes, and data seeding. Key hubs include:
- `ForumHub`: Real-time forum updates
- `TranslationHub`: Translation status synchronization
- `SetupHub`: Configuration change notifications
- `SeedingHub`: Data seeding progress tracking

## Configuration
1. **Startup Setup**
```csharp
// In Program.cs or Startup.cs
app.UseEndpoints(endpoints => {
    endpoints.MapHub<ForumHub>("/forumHub");
    endpoints.MapHub<TranslationHub>("/translationHub");
    // Add other hubs
});
```

2. **Dependency Injection**
```csharp
// In ServiceCollectionExtensions.cs
services.AddSignalR();
```

## Hub Implementation Examples
### ForumHub.cs
```csharp
public class ForumHub : Hub
{
    public async Task SendThreadUpdate(string threadId, string message)
    {
        await Clients.Others.SendAsync("ReceiveThreadUpdate", threadId, message);
    }
}
```

### TranslationHub.cs
```csharp
public class TranslationHub : Hub
{
    public async Task ReportTranslationProgress(string jobId, float progress)
    {
        await Clients.User(jobId).SendAsync("UpdateTranslationProgress", progress);
    }
}
```

## Client Usage
### JavaScript Client
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/forumHub")
    .build();

connection.on("ReceiveThreadUpdate", (threadId, message) => {
    console.log(`Thread ${threadId} updated: ${message}`);
});

connection.start().catch(err => console.error(err));
```

## Best Practices
1. Use `IUserIdProvider` for user-specific notifications
2. Implement `HubFilter` for request validation
3. Monitor hub performance with Application Insights
4. Use `Groups` for forum-specific message routing
```