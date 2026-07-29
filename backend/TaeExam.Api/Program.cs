using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddScoped<ExamImportParsingService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<AuditLogChannel>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddHostedService<AuditLogWriter>();

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var seedDataPath = app.Configuration["SeedDataPath"]
        ?? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "seed"));
    DbSeeder.Seed(db, seedDataPath);
}

app.MapGet("/", () => "TAE Exam API is running.");
app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapRolePermissionsEndpoints();
app.MapAuditLogsEndpoints();
app.MapAnnouncementsEndpoints();
app.MapExamCategoriesEndpoints();
app.MapExamImportEndpoints();
app.MapWrongQuestionsEndpoints();
app.MapSyllabusEndpoints();
app.MapExamsEndpoints();
app.MapAttemptsEndpoints();
app.MapAnalysisEndpoints();

app.Run();
