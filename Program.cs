// Program.cs - ASP.NET Core REST API
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Movex.API;
using Movex.API.Middleware;
using Movex.API.Core.Interfaces;
using Movex.API.Infrastructure.Configuration;
using Movex.API.Infrastructure.Resilience;
using Movex.API.Infrastructure.TransactionBuilding;
using Movex.API.Services;
using Movex.API.Services.Validation;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var isDev = builder.Environment.IsDevelopment();
var isSwaggerGeneration = builder.Configuration.GetValue<bool>("Swagger:Generate")
    || string.Equals(Environment.GetEnvironmentVariable("SWAGGER_GENERATION"), "true", StringComparison.OrdinalIgnoreCase);

// Configuration: User Secrets in Development, plus Environment Variables
if (isDev)
{
    builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
}
builder.Configuration.AddEnvironmentVariables();

// Configure Serilog for file logging
var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
if (!Directory.Exists(logsDirectory))
{
    Directory.CreateDirectory(logsDirectory);
}

builder.Host.UseSerilog((context, services, configuration) =>
{
    var logLevel = context.Configuration["Logging:LogLevel:Default"] ?? "Information";
    var minLevel = logLevel switch
    {
        "Debug" => LogEventLevel.Debug,
        "Information" => LogEventLevel.Information,
        "Warning" => LogEventLevel.Warning,
        "Error" => LogEventLevel.Error,
        _ => LogEventLevel.Information
    };

    configuration
        .MinimumLevel.Is(minLevel)
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine(logsDirectory, "movex-api-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            fileSizeLimitBytes: 104857600) // 100 MB per file
        .Enrich.FromLogContext();
});

var m3Host = builder.Configuration["M3:HOST"]
    ?? (isSwaggerGeneration ? "swagger" : throw new InvalidOperationException("M3:HOST is not configured"));
var m3PortSetting = builder.Configuration["M3:PORT"];
var m3Port = int.TryParse(m3PortSetting, out var parsedPort)
    ? parsedPort
    : (isSwaggerGeneration ? 0 : 6300);
var m3User = builder.Configuration["M3:USER"]
    ?? (isSwaggerGeneration ? "swagger" : throw new InvalidOperationException("M3:USER is not configured"));
var m3Password = builder.Configuration["M3:PASSWORD"]
    ?? (isSwaggerGeneration ? "swagger" : throw new InvalidOperationException("M3:PASSWORD is not configured"));
var defaultCompany = builder.Configuration["M3:DEFAULT_COMPANY"] ?? "300";
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

// Connection pool configuration
var poolOptions = new MvxConnectionPoolOptions
{
    MinPoolSize = int.Parse(builder.Configuration["ConnectionPool:MinSize"] ?? "2"),
    MaxPoolSize = int.Parse(builder.Configuration["ConnectionPool:MaxSize"] ?? "10"),
    ConnectionIdleTimeoutSeconds = int.Parse(builder.Configuration["ConnectionPool:IdleTimeoutSeconds"] ?? "300"),
    AcquireTimeoutSeconds = int.Parse(builder.Configuration["ConnectionPool:AcquireTimeoutSeconds"] ?? "10"),
    ValidateOnAcquire = bool.Parse(builder.Configuration["ConnectionPool:ValidateOnAcquire"] ?? "true")
};

if (isSwaggerGeneration)
{
    poolOptions.MinPoolSize = 0;
    poolOptions.MaxPoolSize = 0;
    poolOptions.ConnectionIdleTimeoutSeconds = 1;
    poolOptions.AcquireTimeoutSeconds = 1;
    poolOptions.ValidateOnAcquire = false;
}

// API Key config
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKeys"));

Console.WriteLine($"[Config] Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"[Config] Swagger Generation Mode: {(isSwaggerGeneration ? "enabled" : "disabled")}");

Console.WriteLine($"[Config] User Secrets: {(isDev ? "enabled" : "disabled (set ASPNETCORE_ENVIRONMENT=Development to use)")}");
Console.WriteLine($"[Config] Env Vars: enabled");
Console.WriteLine($"[Config] Host: {m3Host}, Port: {m3Port}, User: {(m3User != "your-username" ? "***" : "not set")}");
Console.WriteLine($"[Config] Connection Pool: Min={poolOptions.MinPoolSize}, Max={poolOptions.MaxPoolSize}, IdleTimeout={poolOptions.ConnectionIdleTimeoutSeconds}s");
Console.WriteLine(allowedOrigins.Length > 0
    ? $"[Config] CORS: Allowed origins = {string.Join(", ", allowedOrigins)}"
    : "[Config] CORS: No allowed origins configured; cross-origin requests will be blocked");
Console.WriteLine($"[Config] Transaction Config: External configs loaded from 'transactions/' directory");

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Movex REST API",
        Version = "v1",
        Description = "Config-driven REST API for M3 MOVEX transactions and workflows."
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API key required. Use the X-API-Key header.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.AddSecurityDefinition("AdminKey", new OpenApiSecurityScheme
    {
        Description = "Admin key for rotation endpoints. Use the X-Admin-Key header.",
        Name = "X-Admin-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

// Add validation services
builder.Services.AddSingleton<IFieldValidator, FieldValidator>();

// Add transaction string builder
builder.Services.AddSingleton<ITransactionStringBuilder, TransactionStringBuilder>();

// Add transaction configuration service (loads from transactions/ directory)
builder.Services.AddSingleton<ITransactionConfigurationService, TransactionConfigurationService>(provider =>
    new TransactionConfigurationService("transactions"));

// Add response parser
builder.Services.AddSingleton<IResponseParser, M3ResponseParser>();

// Add item validation service
builder.Services.AddScoped<IItemValidationService, ItemValidationService>();

// Add connection pool
builder.Services.AddSingleton<MvxConnectionPool>(provider =>
    new MvxConnectionPool(m3Host, m3Port, m3User, m3Password, "MVXCOBJ", "MMS200MI", poolOptions));

// Add Polly resilience policies (retry + circuit breaker)
builder.Services.AddSingleton(provider => PollyPolicies.GetCombinedPolicyAsync());

// Add M3 transaction service (config-driven)
builder.Services.AddScoped<ITransactionService, TransactionService>(provider =>
    new TransactionService(
        provider.GetRequiredService<MvxConnectionPool>(),
        provider.GetRequiredService<ITransactionConfigurationService>(),
        provider.GetRequiredService<ITransactionStringBuilder>(),
        provider.GetRequiredService<IResponseParser>(),
        provider.GetRequiredService<ILogger<TransactionService>>(),
        provider.GetRequiredService<Polly.IAsyncPolicy>(),
        m3Host,
        m3Port,
        m3User,
        m3Password));

// CORS (restrict if AllowedOrigins provided)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Disallow cross-origin requests when not explicitly configured
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Movex REST API v1");
    options.RoutePrefix = "swagger";
});

app.UseCors();

// API Key auth (skip health)
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseRouting();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => new
{
    status = "ok",
    timestamp = DateTime.UtcNow,
    config = new
    {
        host = m3Host,
        port = m3Port,
        username = m3User != "your-username" ? "***" : "not set"
    }
});

// Connection pool stats endpoint (requires API key)
app.MapGet("/api/transactions/pool-stats", (ITransactionService service) => new
{
    success = true,
    stats = service.GetPoolStats(),
    timestamp = DateTime.UtcNow
});

// Get available programs from config service
var configService = app.Services.GetRequiredService<ITransactionConfigurationService>();
var programs = configService.GetPrograms();

Console.WriteLine($"\n{new string('=', 60)}");
Console.WriteLine($"MMS200MI REST API (using mvxlib opensource project)");
Console.WriteLine($"{new string('=', 60)}");
Console.WriteLine($"Server: http://localhost:5000");
Console.WriteLine($"M3 Server: {m3Host}:{m3Port}");
Console.WriteLine($"Available programs: {string.Join(", ", programs)}");
Console.WriteLine($"{new string('=', 60)}\n");

app.Run();

// Needed for AddUserSecrets<Program>() in top-level statements
public partial class Program { }
