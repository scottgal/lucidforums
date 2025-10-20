var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Bind Ollama options from configuration and environment
builder.Services.Configure<LucidForums.Services.Llm.OllamaOptions>(builder.Configuration.GetSection("Ollama"));

// Register LLM services
builder.Services.AddSingleton<LucidForums.Services.Llm.IOllamaEndpointProvider, LucidForums.Services.Llm.OllamaEndpointProvider>();
builder.Services.AddHttpClient("ollama", (sp, client) =>
{
    var ep = sp.GetRequiredService<LucidForums.Services.Llm.IOllamaEndpointProvider>();
    client.BaseAddress = ep.GetBaseAddress();
});
builder.Services.AddSingleton<LucidForums.Services.Llm.IOllamaChatService, LucidForums.Services.Llm.OllamaChatService>();
builder.Services.AddSingleton<LucidForums.Services.Moderation.IModerationService, LucidForums.Services.Moderation.ModerationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();