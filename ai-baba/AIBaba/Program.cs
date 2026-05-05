using AIBaba.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC + API controllers
builder.Services.AddControllersWithViews();

// CORS (so the SPA-style frontend can call /api from anywhere if hosted separately)
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// Ollama
var ollamaSettings = new OllamaSettings();
builder.Configuration.GetSection("Ollama").Bind(ollamaSettings);
builder.Services.AddSingleton(ollamaSettings);
builder.Services.AddHttpClient<IOllamaService, OllamaService>();

// Memory store (singleton; in-memory)
builder.Services.AddSingleton<IMemoryStore, MemoryStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors();

app.UseMiddleware<SessionMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
