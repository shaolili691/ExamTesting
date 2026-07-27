using Microsoft.EntityFrameworkCore;
using TaeExam.Api.Data;
using TaeExam.Api.Endpoints;
using TaeExam.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Server=localhost;Database=taeExam;User=root;Password=aisddi123;";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<PaperGenerationService>();
builder.Services.AddScoped<DrillGenerationService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var seedDataPath = app.Configuration["SeedDataPath"]
        ?? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "seed"));
    DbSeeder.Seed(db, seedDataPath);
}

app.MapGet("/", () => "TAE Exam API is running.");
app.MapSyllabusEndpoints();
app.MapExamsEndpoints();
app.MapAttemptsEndpoints();
app.MapAnalysisEndpoints();

app.Run();
