# Database Setup Guide

## Quick Start

The application will **automatically create the database** on first run if it doesn't exist. You just need PostgreSQL running!

### Option 1: Using Docker (Recommended)

```bash
# Start PostgreSQL with pgvector extension
docker compose up -d db

# Or start all services (PostgreSQL, Ollama, and the app)
docker compose up -d
```

### Option 2: Local PostgreSQL

1. **Install PostgreSQL 16** with pgvector extension
2. **Update connection string** in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=lucidforums;Username=postgres;Password=YOUR_PASSWORD"
  }
}
```

3. **Run the application** - the database will be created automatically

```bash
cd LucidForums
dotnet run
```

## What Happens on First Run?

The application automatically:

1. ✅ **Creates the database** if it doesn't exist
2. ✅ **Runs migrations** to create tables
3. ✅ **Installs PostgreSQL extensions**:
   - `ltree` - for hierarchical threading
   - `vector` (pgvector) - for semantic search
4. ✅ **Creates additional tables**:
   - `message_embeddings` - for vector search
   - `AppSettings` - for runtime config
5. ✅ **Seeds sample data**:
   - 8 charter templates
   - Dev admin user (Development mode only)

## Connection String Format

```
Host=<hostname>;Port=<port>;Database=<dbname>;Username=<user>;Password=<password>
```

### Examples

**Local PostgreSQL:**
```
Host=localhost;Port=5432;Database=lucidforums;Username=postgres;Password=postgres
```

**Docker Compose:**
```
Host=db;Port=5432;Database=lucidforums;Username=lucid;Password=lucidpassword
```

**Cloud (e.g., Neon, Supabase):**
```
Host=your-project.neon.tech;Database=lucidforums;Username=user;Password=pass;SSL Mode=Require
```

## Required PostgreSQL Extensions

The application requires these PostgreSQL extensions:

### 1. ltree (Hierarchical Data)
Used for efficient threaded message storage.

```sql
CREATE EXTENSION IF NOT EXISTS ltree;
```

### 2. pgvector (Vector Search)
Used for semantic search with AI embeddings.

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

**Note:** Both extensions are installed automatically when you run the app.

## Database Schema

The application creates these tables:

### Core Tables
- `AspNetUsers` - User accounts (Identity)
- `AspNetRoles` - User roles (Identity)
- `Forums` - Forum communities
- `Threads` - Discussion threads
- `Messages` - Individual posts (uses `ltree` for hierarchy)
- `Charters` - Community governance rules
- `ForumUsers` - Forum memberships (many-to-many)
- `AppSettings` - Runtime configuration

### Search Tables
- `message_embeddings` - Vector embeddings for semantic search
  - Indexed with IVFFlat for fast similarity search

## Troubleshooting

### Error: "database does not exist"

**✅ Fixed!** The application now automatically creates the database. Just ensure PostgreSQL is running.

### Error: "extension vector does not exist"

Install pgvector:

**Docker:** Use `pgvector/pgvector:pg16` image (already in docker-compose.yaml)

**Linux:**
```bash
sudo apt install postgresql-16-pgvector
```

**macOS:**
```bash
brew install pgvector
```

**Windows:** Download from https://github.com/pgvector/pgvector/releases

### Error: "permission denied"

Your PostgreSQL user needs these permissions:
- `CREATE DATABASE` - to create the database
- `CREATE EXTENSION` - to install ltree and vector

Grant permissions:
```sql
ALTER USER your_user CREATEDB;
ALTER USER your_user WITH SUPERUSER; -- or grant CREATE EXTENSION privilege
```

### Database Connection Issues

1. **Check PostgreSQL is running:**
   ```bash
   # Docker
   docker compose ps

   # Linux
   sudo systemctl status postgresql

   # macOS
   brew services list
   ```

2. **Test connection:**
   ```bash
   psql -h localhost -U postgres -d postgres
   ```

3. **Check firewall** - ensure port 5432 is open

## Manual Database Setup (Optional)

If you want to set up the database manually:

```bash
# Connect to PostgreSQL
psql -U postgres

# Create database
CREATE DATABASE lucidforums;

# Connect to the new database
\c lucidforums

# Install extensions
CREATE EXTENSION IF NOT EXISTS ltree;
CREATE EXTENSION IF NOT EXISTS vector;

# Exit
\q
```

Then run the application - it will create tables via migrations.

## Development Database Reset

To start fresh:

```bash
# Option 1: Use Admin UI
# Navigate to /Admin/Tools → Clear All Content

# Option 2: Drop and recreate database
psql -U postgres -c "DROP DATABASE IF EXISTS lucidforums;"
psql -U postgres -c "CREATE DATABASE lucidforums;"

# Option 3: Delete via Admin Maintenance
# Navigate to /AdminMaintenance → Clear All Content
```

Then restart the app - it will reinitialize everything.

## Production Considerations

For production deployments:

1. **Use strong passwords** - never use default passwords
2. **Use SSL/TLS** - add `SSL Mode=Require` to connection string
3. **Use connection pooling** - already configured via Npgsql
4. **Backup strategy** - set up automated backups
5. **Monitoring** - monitor connection pool, query performance
6. **Resource limits** - set appropriate max_connections in PostgreSQL

### Recommended PostgreSQL Settings

```conf
# postgresql.conf
max_connections = 100
shared_buffers = 256MB
effective_cache_size = 1GB
maintenance_work_mem = 64MB
random_page_cost = 1.1  # For SSD
effective_io_concurrency = 200
```

## Environment Variables

You can override the connection string via environment variables:

```bash
# Linux/macOS
export ConnectionStrings__Default="Host=localhost;Database=lucidforums;..."

# Windows PowerShell
$env:ConnectionStrings__Default="Host=localhost;Database=lucidforums;..."

# Docker
docker run -e ConnectionStrings__Default="..." lucidforums
```

## User Secrets (Development)

For local development, use User Secrets to avoid committing passwords:

```bash
cd LucidForums
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=lucidforums;Username=postgres;Password=YOUR_PASSWORD"
```

The application will automatically use this instead of appsettings.json.
