using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Tulaziva.Api.Endpoints;
using Tulaziva.Api.Middleware;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Tulaziva.Api;
using Tulaziva.Api.Realtime;
using Tulaziva.Application;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Localization;
using Tulaziva.Infrastructure;
using Tulaziva.Infrastructure.Persistence;
using Tulaziva.Infrastructure.Security;

const string CorsPolicy = "Default";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Live chat: the API pushes new messages and read marks to the connected users. It registers after AddApplication, so it replaces the "does nothing" notifier.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

// The endpoints that send an email are limited per IP, so they cannot be used to flood somebody's inbox.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.AccountEmails, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AccountEmailsPermitLimit", 5),
            Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:AccountEmailsWindowMinutes", 10)),
            QueueLimit = 0
        }));
});

// CORS: only the origins listed in Cors:AllowedOrigins can call the API from a browser.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Authentication: JWT bearer. The claims keep their names in the token ("sub", "role"), they are not remapped.
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

if (jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters. Set it with user secrets or an environment variable.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        // Browsers cannot set headers on a WebSocket, so the live channel sends the token in the query string.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(ChatHub.Path))
                    context.Token = token;

                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy(AdminEndpoints.AdminPolicy, policy => policy.RequireRole(AdminEndpoints.AdminRole)));

// Swagger with a button to paste the token.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Tulaziva API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token returned by /api/auth/login or /api/auth/register."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// The language comes from the Accept-Language header (es or en, es by default). It goes before the exception
// middleware so the error messages are also written in that language.
var cultures = SupportedLanguages.All.ToArray();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(SupportedLanguages.Default)
    .AddSupportedCultures(cultures)
    .AddSupportedUICultures(cultures));

app.UseMiddleware<ExceptionHandlingMiddleware>();

// The browser must trust the content type the API sends and never guess it: an uploaded file is never run as a page or a script.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// In development the web app reaches the API through the Vite proxy over plain HTTP; a redirect to HTTPS would send the browser to
// another origin (and a preflight cannot follow a redirect), so it is only used outside development.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAdEndpoints();
app.MapChatEndpoints();
app.MapAdminEndpoints();
app.MapFavoriteEndpoints();
app.MapReportEndpoints();
app.MapBlockEndpoints();
app.MapHub<ChatHub>(ChatHub.Path);

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.Run();

// Lets integration tests start the API with WebApplicationFactory<Program>.
public partial class Program { }
