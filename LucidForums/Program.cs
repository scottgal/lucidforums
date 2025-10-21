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
    // Fallback to a runtime mapper so the app can run without the source generator
    builder.Services.AddSingleton<LucidForums.Web.Mapping.IAppMapper, LucidForums.Web.Mapping.RuntimeAppMapper>();
}

var app = builder.Build();

app
    .UseLucidForumsPipeline()
    .MapLucidForumsEndpoints()
    .InitializeLucidForumsDatabase();

app.Run();