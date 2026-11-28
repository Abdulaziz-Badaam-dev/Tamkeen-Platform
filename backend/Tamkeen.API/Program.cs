using System.Threading.RateLimiting;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Controllers;
using Tamkeen.API.Data;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services;
using Tamkeen.API.Services.Auth;
using Tamkeen.API.Services.AiAgent;
using Tamkeen.API.Services.AiAgent.Clients;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Tools;

var builder = WebApplication.CreateBuilder(args);
var mvc = builder.Services.AddControllers();
if (!builder.Environment.IsDevelopment())
    mvc.ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ProductionControllers()));
builder.Services.AddDbContext<TamkeenDb>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<TamkeenDb>().AddSignInManager();
builder.Services.AddSingleton<JwtTokens>();
builder.Services.AddOptions<GoogleJsonWebSignature.ValidationSettings>().Configure<IConfiguration>((options, config) =>
{
    var clientId = config["Authentication:Google:ClientId"];
    options.Audience = string.IsNullOrWhiteSpace(clientId) ? [] : [clientId];
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtTokens>((options, tokens) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.Validation();
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ??
        (builder.Environment.IsDevelopment() ? ["http://localhost:5173", "http://localhost:3000"] : []);
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
        .WithExposedHeaders("X-Submission-Truncated", "Content-Disposition");
}));
builder.Services.AddScoped<TrackWorkflow>();
builder.Services.AddSingleton<CertificatePdf>();
builder.Services.AddExceptionHandler<WriteConflictHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("evaluation", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});
foreach (var (name, timeoutSeconds) in new[] { ("github", 60), ("gemini", 150) })
{
    builder.Services.AddHttpClient(name, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tamkeen-Evaluation/1.0");
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
}
builder.Services.AddScoped<SubmissionReader>();
builder.Services.AddScoped<IAiModelClient, GeminiModelClient>();
builder.Services.AddScoped<IEvaluationAgentService, EvaluationAgentService>();
builder.Services.AddSingleton<IAgentTool, SubmissionCodeTool>();
builder.Services.AddSingleton<IAgentTool, StaticSecurityScanTool>();
builder.Services.AddScoped<IAgentTool, PreviousAttemptTool>();
builder.Services.AddSingleton<IAgentTool, ComplianceMetadataTool>();
builder.Services.AddScoped<IPreviousAttempts, DatabasePreviousAttempts>();
var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<JwtTokens>();
    var db = scope.ServiceProvider.GetRequiredService<TamkeenDb>();
    if (db.Database.IsSqlServer()) db.Database.Migrate();
    else db.Database.EnsureCreated();
}
app.Run();

public partial class Program { }
