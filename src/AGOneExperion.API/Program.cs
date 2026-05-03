using AGOneExperion.Infrastructure;
using AGOneExperion.API.Middleware;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ── CORS ─────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ── Controllers ───────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── Swagger ───────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AG ONE Experion API",
        Version = "v2.0",
        Description = "Activity Mining + Recommendation Agent + Contextual AI for any website."
    });
});

// ── Experion Infrastructure (all services wired here) ─────────────────
builder.Services.AddExperionInfrastructure(builder.Configuration);

// ── HTTP Logging ──────────────────────────────────────────────────────
builder.Services.AddHttpLogging(_ => { });

var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────
app.UseCors();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Experion v2");
        c.RoutePrefix = string.Empty;
    });
}

app.UseAuthorization();
app.MapControllers();

app.Run();
