using LucidForums.Extensions;
using Serilog;

// Bootstrap Serilog early
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for Host logging, read configuration
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// MVC, SignalR, DB/Identity, configuration POCOs, AI, domain services, etc.
builder.Services.AddLucidForumsAll(builder.Configuration);

// Mapster mapping configuration and mapper registration
LucidForums.Web.Mapping.MapsterRegistration.Register(Mapster.TypeAdapterConfig.GlobalSettings);
var mapperImpl = Type.GetType("LucidForums.Web.Mapping.AppMapper, LucidForums");
if (mapperImpl is not null)
{
    builder.Services.AddSingleton(typeof(LucidForums.Web.Mapping.IAppMapper), mapperImpl);
}
else
{
    builder.Services.AddSingleton<LucidForums.Web.Mapping.IAppMapper>(_ => throw new InvalidOperationException("Mapster source-generated mapper not found. Ensure Mapster.SourceGenerator is installed and the project is built."));
}

var app = builder.Build();

app
    .UseLucidForumsPipeline()
    .MapLucidForumsEndpoints()
    .InitializeLucidForumsDatabase();

app.Run();