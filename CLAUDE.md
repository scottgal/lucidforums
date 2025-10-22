# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

LucidForums is a forum/chat hybrid platform built with ASP.NET Core 9.0 and PostgreSQL. It features AI-assisted moderation, charter-based community governance, and semantic search capabilities. The frontend uses HTMX + Alpine.js with Tailwind CSS/DaisyUI for a lightweight, reactive experience.

## Common Commands

### Backend (.NET)

```bash
# Build the solution
dotnet build LucidForums.sln

# Run the application (from LucidForums/LucidForums directory)
cd LucidForums/LucidForums
dotnet run

# Run tests
dotnet test LucidForums.sln

# Run a single test file
dotnet test LucidForums.Tests/Path/To/TestFile.cs

# Run tests with filter
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Restore dependencies
dotnet restore
```

### Frontend (JavaScript/CSS)

Frontend assets are in `LucidForums/LucidForums/src/` and built to `wwwroot/`.

```bash
# Install dependencies (from LucidForums/LucidForums directory)
cd LucidForums/LucidForums
npm install

# Development build (one-time)
npm run dev

# Watch mode (rebuilds on file changes)
npm run watch

# Production build (minified)
npm run build
```

The `npm run dev`/`watch`/`build` commands run webpack (for JS) and Tailwind CSS compilation in parallel.

### Docker

```bash
# Start all services (PostgreSQL with pgvector, Ollama, app)
docker compose up -d

# View logs
docker compose logs -f

# Stop services
docker compose down

# Rebuild app container
docker compose build lucidforums

# Pull Ollama models (after services are running)
docker compose exec ollama ollama pull llama3.2:1b
docker compose exec ollama ollama pull mxbai-embed-large
```

## Architecture Overview

### Layered Architecture

The codebase follows a clean layered architecture with dependency injection:

```
Controllers (HTTP/SignalR endpoints)
    ↓
Services (business logic organized by domain)
    ├── Forum domain: IForumService, IThreadService, IMessageService
    ├── AI services: ITextAiService, IImageAiService, IAiSettingsService
    ├── Analysis: ICharterScoringService, ITagExtractionService, IToneAdvisor
    └── Search: IEmbeddingService (semantic search with pgvector)
    ↓
Data Access (ApplicationDbContext)
    ↓
PostgreSQL (with ltree, pgvector, full-text search)
```

### Service Registration Pattern

All DI registration happens via extension methods in `ServiceCollectionExtensions.cs`:

```csharp
services
    .AddLucidForumsMvcAndRealtime()     // MVC + SignalR
    .AddLucidForumsConfiguration()      // Configuration POCOs
    .AddLucidForumsDatabase()           // EF Core DbContext
    .AddLucidForumsAi()                 // AI providers
    .AddLucidForumsSeeding()            // Background seeding job
    .AddLucidForumsModeration()         // Moderation service
    .AddLucidForumsEmbedding()          // Semantic search
    .AddLucidForumsDomainServices()     // Core business logic
    .AddLucidForumsObservability();     // Telemetry
```

When adding new services, follow this pattern and add to the appropriate extension method.

### Database: PostgreSQL with Advanced Features

#### Entity Model

- **Forum**: Communities with optional charters
- **ForumThread**: Discussion threads with AI-generated tags and charter scores
- **Message**: Individual posts in threads, organized hierarchically
- **Charter**: Community governance rules (purpose, values, behaviors)
- **User**: ASP.NET Identity user with forum memberships
- **ForumUser**: Many-to-many join table (Forum ↔ User)
- **AppSettings**: Runtime AI configuration (singleton with Id=1)

#### PostgreSQL Extensions

1. **ltree**: Hierarchical threading using materialized paths
   - `Message.Path` stores ancestry as `{id1}.{id2}.{id3}`
   - GIST index enables efficient ancestor/descendant queries
   - Example: Find all replies to a message: `WHERE Path @> 'parent_id'`

2. **pgvector**: Semantic search with embeddings
   - `message_embeddings` table stores 1024-dimensional vectors
   - IVFFlat index for cosine similarity search
   - Used for "find similar discussions" and deduplication

3. **Full-text search**: Native PostgreSQL text search (planned)

### AI Integration Architecture

#### Pluggable Provider System

The system supports multiple AI providers via `IChatProvider` interface:

- **OllamaChatProvider**: Local LLM via Ollama (default)
- **LmStudioChatProvider**: LM Studio compatibility
- Future: OpenAI, Anthropic, etc.

Providers are hot-swappable via runtime configuration (stored in `AppSettings` table).

#### AI-Driven Features

1. **Charter Scoring**: Scores content 0-100 for alignment with community charter
2. **Tag Extraction**: Generates up to 8 relevant tags per thread
3. **Tone Advice**: Provides friendly moderation suggestions appended to messages
4. **Content Generation**: Seeding system generates realistic forum content

All AI calls include charter-based system prompts for context-aware decisions.

#### Configuration

AI settings are in `appsettings.json` under `AI`, `Ollama`, `LmStudio` sections. Runtime settings stored in database via `AdminAiSettingsController`.

### Frontend: HTMX + Alpine.js

#### View Organization

Views follow MVC structure in `Views/`:
- `Shared/_Layout.cshtml`: Master layout with navbar, theme switcher
- Controller-specific views (e.g., `Forum/`, `Threads/`)
- Partial views for HTMX responses (prefixed with `_`)

#### HTMX Pattern

Controllers detect HTMX requests and return partials:

```csharp
if (Request.Headers.TryGetValue("HX-Request", out var hx) && hx == "true")
    return PartialView("_ThreadList", vm);  // Partial for HTMX
return View(vm);                             // Full page
```

#### Real-time Updates

SignalR hubs broadcast events (e.g., `ForumHub` for new messages). Alpine.js components listen and update DOM:

```javascript
// In wwwroot/js/forum-thread.js
connection.on("NewMessage", async (messageId) => {
    // Fetch HTML snippet and insert
});
```

#### JavaScript Structure

Frontend code is in `src/js/`:
- `main.js`: Entry point (imports all modules)
- `htmx-events.js`: HTMX event handlers
- `theme-switcher.js`: Dark/light mode toggle
- `pages/`: Page-specific logic (admin AI settings, setup flow, etc.)

Build via webpack → `wwwroot/js/bundle.js`

### Mapping: Mapster

DTO-to-ViewModel mapping configured in `Web/Mapping/MapsterRegistration.cs`. Two implementations:
- `AppMapper`: Source-generated (compile-time)
- `RuntimeAppMapper`: Fallback runtime mapper

Inject `IAppMapper` in services to use.

### Background Services

`ForumSeedingHostedService` generates realistic forum content:
- Reads from `IForumSeedingQueue` (channel-based queue)
- Creates forums, threads, and messages with AI
- De-duplicates via embedding similarity (cosine threshold 0.90)
- Broadcasts progress via `SeedingHub` (SignalR)

### Observability

OpenTelemetry integration captures:
- **Traces**: HTTP requests, database calls, AI calls
- **Metrics**: Request latency, AI provider performance
- **Logs**: Serilog with structured logging

Export to OTLP endpoint (default: `localhost:4317`) for Jaeger, Honeycomb, etc.

## Key Patterns and Conventions

### Configuration Hot-Reloading

Two patterns:
1. **Static**: `ConfigurePOCO<T>()` - Loaded once at startup
2. **Scoped/Reloadable**: `ConfigureScopedPOCOFromMonitor<T>()` - Tracks changes via `IOptionsMonitor`

Use `IOptionsMonitor<T>` in services that need runtime config updates.

### Best-Effort Analysis

AI-driven features (tagging, scoring, tone advice) are wrapped in try-catch and never block core operations. Failures are logged but don't prevent content creation.

### Fire-and-Forget Embedding Indexing

After creating threads/messages, embedding generation happens in background:

```csharp
_ = Task.Run(async () => {
    await _embeddingService.IndexMessageAsync(messageId);
}, CancellationToken.None);
```

This avoids blocking user requests for expensive operations.

### Charter-Based Governance

Every AI call receives a system prompt generated from the community's charter (purpose, rules, behaviors). This ensures AI decisions align with community values.

### Hierarchical Message Threading

Use `Message.Path` (ltree) for queries:
- Get all descendants: `WHERE Path <@ 'parent.path'`
- Get direct children: `WHERE ParentId = @id`
- Order by path: `ORDER BY Path` (maintains hierarchy)

Always update path when creating replies: `{parent.Path}.{new_message_id}`

## Testing

Tests use xUnit with FluentAssertions. Located in `LucidForums.Tests/`.

When writing tests:
- Use `FluentAssertions` for readable assertions
- Mock services via interfaces (all domain services are interface-based)
- Use in-memory database for integration tests (optional)

## Environment Setup

### Local Development

1. **Install .NET 9.0 SDK**
2. **Install Node.js** (for frontend builds)
3. **Install PostgreSQL** with pgvector extension OR use Docker Compose
4. **Install Ollama** (for AI features) and pull models:
   ```bash
   ollama pull llama3.2:1b
   ollama pull mxbai-embed-large
   ```
5. **Copy `.env.example` to `.env`** and adjust if needed
6. **Run migrations** (automatic on startup via `InitializeLucidForumsDatabase()`)

### Docker Development (Recommended)

```bash
docker compose up -d
# Pull models after Ollama starts
docker compose exec ollama ollama pull llama3.2:1b
docker compose exec ollama ollama pull mxbai-embed-large
```

Access app at `http://localhost:8080`

### User Secrets

User secrets ID: `fb9d7dea-fdf9-4711-b889-f0db3486f53a`

Store sensitive config (API keys, etc.) via:
```bash
dotnet user-secrets set "OpenAI:ApiKey" "your-key-here" --project LucidForums/LucidForums
```

## Important Architectural Decisions

1. **ltree over Nested Sets**: Materialized path pattern is simpler and more flexible for forum threading
2. **pgvector for Semantic Search**: Enables "find similar" without explicit tagging
3. **HTMX over SPA**: Reduces JavaScript complexity while maintaining interactivity
4. **Best-Effort AI**: Never block core operations on AI failures
5. **Pluggable AI Providers**: Future-proofs against LLM ecosystem changes
6. **Charter-Driven AI**: Moves moderation policies from code to data
7. **SignalR for Real-time**: WebSocket-based live updates without polling

## File Locations

### Configuration
- `appsettings.json`, `appsettings.Development.json`: App configuration
- `.env.example`: Docker Compose environment template
- `docker-compose.yaml`: Container orchestration

### Backend Structure
- `Controllers/`: HTTP/SignalR endpoints
- `Services/`: Business logic (organized by domain)
- `Data/`: EF Core DbContext and migrations
- `Models/`: Entities, ViewModels, DTOs
- `Extensions/`: DI registration and middleware
- `Hubs/`: SignalR hubs
- `Helpers/`: Utility functions
- `Web/Mapping/`: Mapster configuration

### Frontend Structure
- `Views/`: Razor templates
- `src/js/`: JavaScript modules (pre-build)
- `src/css/`: Tailwind CSS source
- `wwwroot/`: Static assets (post-build)

### Tests
- `LucidForums.Tests/`: xUnit test project