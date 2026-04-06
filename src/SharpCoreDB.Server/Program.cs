// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.IO.Compression;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Grpc;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using SharpCoreDB.Server.Core.WebSockets;

// Create and configure the host
var builder = WebApplication.CreateBuilder(args);

var serverConfig = builder.Configuration.GetSection("Server").Get<ServerConfiguration>()
    ?? throw new InvalidOperationException("Server configuration is missing");

if (string.IsNullOrWhiteSpace(serverConfig.Security.JwtSecretKey) || serverConfig.Security.JwtSecretKey.Length < 32)
{
    throw new InvalidOperationException("JWT secret key must be at least 32 characters.");
}

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        "logs/sharpcoredb-server-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

builder.Host.UseSerilog();

// Configure Kestrel for gRPC and HTTPS
builder.WebHost.ConfigureKestrel((context, options) =>
{
    var config = context.Configuration.GetSection("Server").Get<ServerConfiguration>();
    if (config is null)
    {
        throw new InvalidOperationException("Server configuration is missing");
    }

    if (!config.Security.TlsEnabled)
    {
        throw new InvalidOperationException("TLS must be enabled. Plain HTTP is not supported.");
    }

    if (string.IsNullOrWhiteSpace(config.Security.TlsCertificatePath))
    {
        throw new InvalidOperationException("TLS certificate path is required.");
    }

    var tlsVersion = config.Security.MinimumTlsVersion;
    var minimumTls = tlsVersion switch
    {
        "Tls12" => SslProtocols.Tls12 | SslProtocols.Tls13,
        "Tls13" => SslProtocols.Tls13,
        _ => throw new InvalidOperationException($"Unsupported TLS version: {tlsVersion}"),
    };

    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = minimumTls;

        // Mutual TLS: request client certificates when enabled
        if (config.Security.EnableMutualTls)
        {
            httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
        }
    });

    // gRPC endpoint
    if (config.EnableGrpc)
    {
        options.ListenAnyIP(config.GrpcPort, listenOptions =>
        {
            listenOptions.Protocols = config.EnableGrpcHttp3
                ? HttpProtocols.Http2 | HttpProtocols.Http3
                : HttpProtocols.Http2;
            listenOptions.UseHttps(config.Security.TlsCertificatePath, config.Security.TlsPrivateKeyPath);
        });
    }

    // HTTPS API endpoint
    if (config.EnableHttpsApi)
    {
        options.ListenAnyIP(config.HttpsApiPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            listenOptions.UseHttps(config.Security.TlsCertificatePath, config.Security.TlsPrivateKeyPath);
        });
    }
});

// Configure JWT authentication
var jwtKey = Encoding.ASCII.GetBytes(serverConfig.Security.JwtSecretKey);
var tokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
    ValidateIssuer = false,
    ValidateAudience = false,
    ClockSkew = TimeSpan.Zero,
    ValidateLifetime = true,
};

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = tokenValidationParameters;
    options.Audience = "sharpcoredb-server";
    options.Authority = null; // Self-issued tokens
    options.IncludeErrorDetails = builder.Environment.IsDevelopment();
    options.MapInboundClaims = true;
})
.AddCertificate(options =>
{
    options.AllowedCertificateTypes = serverConfig.Security.EnableMutualTls
        ? CertificateTypes.All
        : CertificateTypes.Chained;
    options.RevocationMode = X509RevocationMode.NoCheck;
    options.ValidateCertificateUse = true;
    options.Events = new CertificateAuthenticationEvents
    {
        OnCertificateValidated = context =>
        {
            var certService = context.HttpContext.RequestServices
                .GetRequiredService<CertificateAuthenticationService>();
            var result = certService.ValidateAndMapCertificate(context.ClientCertificate);

            if (result.IsAuthenticated)
            {
                context.Principal = result.Principal;
                context.Success();
            }
            else
            {
                context.Fail(result.ErrorMessage ?? "Certificate validation failed");
            }

            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Log.Warning("Client certificate authentication failed: {Error}", context.Exception?.Message);
            context.Fail(context.Exception?.Message ?? "Certificate authentication failed");
            return Task.CompletedTask;
        },
    };
});

// Configure rate limiting for gRPC (fixed-window per IP)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = serverConfig.Performance.MaxConcurrentQueries,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 100,
            }));

    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
});

// Add services
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaxReceiveMessageSize = 100 * 1024 * 1024; // 100MB
    options.MaxSendMessageSize = 100 * 1024 * 1024;
    options.ResponseCompressionAlgorithm = "gzip";
    options.ResponseCompressionLevel = CompressionLevel.Fastest;
    options.Interceptors.Add<GrpcRequestMetricsInterceptor>();
    options.Interceptors.Add<GrpcAuthorizationInterceptor>();
});

builder.Services.AddGrpcReflection();

// Add MVC for REST API
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// Add server configuration
builder.Services.Configure<ServerConfiguration>(
    builder.Configuration.GetSection("Server"));

// Add core services
builder.Services.AddSingleton<NetworkServer>();
builder.Services.AddSingleton<DatabaseRegistry>();
builder.Services.AddSingleton<TenantQuotaEnforcementService>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<RbacService>();
builder.Services.AddSingleton<UserAuthenticationService>();
builder.Services.AddSingleton<TenantAccessAuditStore>();
builder.Services.AddSingleton<TenantSecurityAuditStore>();
builder.Services.AddSingleton<TenantSecurityAuditService>();
builder.Services.AddSingleton<ITenantEncryptionKeyProvider, ConfigurationTenantEncryptionKeyProvider>();
builder.Services.AddSingleton<TenantEncryptionKeyRotationService>();
builder.Services.AddSingleton<TenantAuthorizationPolicyService>();
builder.Services.AddSingleton<CertificateAuthenticationService>();
builder.Services.AddSingleton<GrpcRequestMetricsInterceptor>();
builder.Services.AddSingleton<GrpcAuthorizationInterceptor>();
builder.Services.AddSingleton(sp => new JwtTokenService(
    serverConfig.Security.JwtSecretKey,
    serverConfig.Security.JwtExpirationHours));

// Add observability services
var metricsCollector = new MetricsCollector("sharpcoredb-server");
builder.Services.AddSingleton(metricsCollector);
builder.Services.AddSingleton<HealthCheckService>();

// Add health checks
builder.Services.AddHealthChecks();

// Add DatabaseService to DI
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddTransient<WebSocketHandler>();

TryConfigureProjectionRuntime(builder.Services, serverConfig);

var app = builder.Build();

// Use authentication and authorization middleware (BEFORE route mapping)
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Enable WebSocket protocol
if (serverConfig.EnableWebSocket)
{
    app.UseWebSockets(new WebSocketOptions
    {
        KeepAliveInterval = TimeSpan.FromSeconds(serverConfig.WebSocketKeepAliveSeconds),
    });

    app.Map(serverConfig.WebSocketPath, async (HttpContext context) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required");
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
        await using (handler)
        {
            await handler.HandleAsync(webSocket, context.RequestAborted);
        }
    });
}

// Map gRPC services
app.MapGrpcService<DatabaseService>();

// Enable gRPC reflection for development
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

// Map REST API controllers
app.MapControllers();

// Map health check endpoint
app.MapHealthChecks("/health");

 // Map REST API endpoints (placeholder)
app.MapGet("/", () => new
{
    name = "SharpCoreDB Server",
    version = "1.5.0",
    status = "running",
    transport = "HTTPS/TLS only",
});

// Map detailed health endpoint
app.MapGet("/api/v1/health/detailed", (HealthCheckService healthService) =>
{
    var health = healthService.GetDetailedHealth();
    return Results.Ok(health);
})
.WithName("DetailedHealth")
.Produces<ServerHealthInfo>(StatusCodes.Status200OK);

// Start the server
Log.Information("Starting SharpCoreDB Server v1.5.0");
Log.Information("🔒 Security Features:");
Log.Information("  • TLS/HTTPS: {TlsVersion} (required, no plain HTTP)", serverConfig.Security.MinimumTlsVersion);
Log.Information("  • JWT Authentication: Enabled (via Bearer token)");
Log.Information("  • Mutual TLS (mTLS): {MtlsEnabled}", serverConfig.Security.EnableMutualTls);
Log.Information("  • RBAC: Admin / Writer / Reader roles");
Log.Information("  • Rate Limiting: {MaxConcurrentQueries} req/10s per IP", serverConfig.Performance.MaxConcurrentQueries);
Log.Information("🚀 Primary protocol (flagship): gRPC");
Log.Information("  • HTTP/3 (QUIC): {GrpcHttp3}", serverConfig.EnableGrpcHttp3);
Log.Information("  • Compression: gzip (fastest)");
Log.Information("  • Server Streaming Batching: 1000 rows/frame");
Log.Information("  • Endpoint: https://{Bind}:{Port}", serverConfig.BindAddress, serverConfig.GrpcPort);
Log.Information("📡 Secondary Protocols:");
Log.Information("  • Binary (PostgreSQL-compatible): {BinaryEnabled} (Port {BinaryPort})", serverConfig.EnableBinaryProtocol, serverConfig.BinaryProtocolPort);
Log.Information("  • HTTP REST API: {HttpEnabled} (Port {HttpPort})", serverConfig.EnableHttpsApi, serverConfig.HttpsApiPort);
Log.Information("  • WebSocket Streaming: {WsEnabled} (Path {WsPath})", serverConfig.EnableWebSocket, serverConfig.WebSocketPath);
Log.Information("📊 Hosted Databases: {Count}", serverConfig.Databases.Count);
foreach (var db in serverConfig.Databases.Take(3))
{
    Log.Information("  • {DatabaseName} ({StorageMode})", db.Name, db.StorageMode);
}
if (serverConfig.Databases.Count > 3)
{
    Log.Information("  ... and {More} more", serverConfig.Databases.Count - 3);
}

if (serverConfig.Projections.Enabled)
{
    Log.Information("🧩 Projection Runtime: Enabled");
    Log.Information("  • Hosted Worker: {Enabled}", serverConfig.Projections.EnableHostedWorker);
    Log.Information("  • Persistent Checkpoints: {Enabled}", serverConfig.Projections.UsePersistentCheckpoints);
    Log.Information("  • OpenTelemetry Projection Metrics: {Enabled}", serverConfig.Projections.UseOpenTelemetryMetrics);
}

try
{
    // Start the network server
    var networkServer = app.Services.GetRequiredService<NetworkServer>();
    await networkServer.StartAsync(app.Lifetime.ApplicationStopping);

    // Run the web host
    await app.RunAsync();
}
catch (InvalidOperationException ex)
{
    Log.Fatal(ex, "SharpCoreDB Server failed startup validation");
}
finally
{
    Log.Information("SharpCoreDB Server shutdown complete");
    await Log.CloseAndFlushAsync();
}

static void TryConfigureProjectionRuntime(IServiceCollection services, ServerConfiguration serverConfig)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(serverConfig);

    if (!serverConfig.Projections.Enabled)
    {
        return;
    }

    var projectionsAssembly = TryLoadOptionalAssembly("SharpCoreDB.Projections");
    var eventSourcingAssembly = TryLoadOptionalAssembly("SharpCoreDB.EventSourcing");
    if (projectionsAssembly is null || eventSourcingAssembly is null)
    {
        Log.Warning("Projection runtime is enabled but optional projection packages are not available. Skipping runtime wiring.");
        return;
    }

    var projectionExtensionsType = projectionsAssembly.GetType("SharpCoreDB.Projections.ProjectionServiceCollectionExtensions");
    var projectionEngineOptionsType = projectionsAssembly.GetType("SharpCoreDB.Projections.ProjectionEngineOptions");
    var projectionHostedServiceOptionsType = projectionsAssembly.GetType("SharpCoreDB.Projections.ProjectionHostedServiceOptions");
    var eventStoreType = eventSourcingAssembly.GetType("SharpCoreDB.EventSourcing.IEventStore");
    var sharpCoreDbEventStoreType = eventSourcingAssembly.GetType("SharpCoreDB.EventSourcing.SharpCoreDbEventStore");

    if (projectionExtensionsType is null ||
        projectionEngineOptionsType is null ||
        projectionHostedServiceOptionsType is null ||
        eventStoreType is null ||
        sharpCoreDbEventStoreType is null)
    {
        Log.Warning("Projection runtime is enabled but required projection/event sourcing types are unavailable. Skipping runtime wiring.");
        return;
    }

    var engineOptions = Activator.CreateInstance(projectionEngineOptionsType)
        ?? throw new InvalidOperationException("Failed to create projection engine options instance.");
    projectionEngineOptionsType.GetProperty("BatchSize")?.SetValue(engineOptions, serverConfig.Projections.BatchSize);
    projectionEngineOptionsType.GetProperty("PollInterval")?.SetValue(engineOptions, TimeSpan.FromMilliseconds(serverConfig.Projections.PollIntervalMilliseconds));
    projectionEngineOptionsType.GetProperty("RunOnStart")?.SetValue(engineOptions, serverConfig.Projections.RunOnStart);
    projectionEngineOptionsType.GetProperty("MaxIterations")?.SetValue(engineOptions, serverConfig.Projections.MaxIterations);
    services.AddSingleton(projectionEngineOptionsType, engineOptions);

    InvokeProjectionExtension(projectionExtensionsType, "AddSharpCoreDBProjections", services, null);

    services.AddSingleton(eventStoreType, serviceProvider =>
    {
        var registry = serviceProvider.GetRequiredService<DatabaseRegistry>();
        var configuredDatabaseName = string.IsNullOrWhiteSpace(serverConfig.Projections.DatabaseName)
            ? serverConfig.DefaultDatabase
            : serverConfig.Projections.DatabaseName;

        var databaseInstance = registry.GetDatabase(configuredDatabaseName)
            ?? throw new InvalidOperationException($"Projection runtime database '{configuredDatabaseName}' is not registered.");

        return Activator.CreateInstance(sharpCoreDbEventStoreType, databaseInstance.Database)
            ?? throw new InvalidOperationException("Failed to create projection event store.");
    });

    if (serverConfig.Projections.UsePersistentCheckpoints)
    {
        services.AddSingleton(typeof(SharpCoreDB.Interfaces.IDatabase), serviceProvider =>
        {
            var registry = serviceProvider.GetRequiredService<DatabaseRegistry>();
            var configuredDatabaseName = string.IsNullOrWhiteSpace(serverConfig.Projections.DatabaseName)
                ? serverConfig.DefaultDatabase
                : serverConfig.Projections.DatabaseName;

            var databaseInstance = registry.GetDatabase(configuredDatabaseName)
                ?? throw new InvalidOperationException($"Projection checkpoint database '{configuredDatabaseName}' is not registered.");

            return databaseInstance.Database;
        });

        InvokeProjectionExtension(projectionExtensionsType, "UseSharpCoreDBProjectionCheckpoints", services, serverConfig.Projections.CheckpointTableName);
    }

    if (serverConfig.Projections.UseOpenTelemetryMetrics)
    {
        InvokeProjectionExtension(projectionExtensionsType, "UseOpenTelemetryProjectionMetrics", services, null, null);
    }

    if (serverConfig.Projections.EnableHostedWorker)
    {
        var hostedServiceOptions = Activator.CreateInstance(projectionHostedServiceOptionsType)
            ?? throw new InvalidOperationException("Failed to create projection hosted service options instance.");

        projectionHostedServiceOptionsType.GetProperty("DatabaseId")?.SetValue(hostedServiceOptions, serverConfig.Projections.RuntimeDatabaseId);
        projectionHostedServiceOptionsType.GetProperty("TenantId")?.SetValue(hostedServiceOptions, serverConfig.Projections.RuntimeTenantId);
        projectionHostedServiceOptionsType.GetProperty("FromGlobalSequence")?.SetValue(hostedServiceOptions, serverConfig.Projections.FromGlobalSequence);
        services.AddSingleton(projectionHostedServiceOptionsType, hostedServiceOptions);

        InvokeProjectionExtension(projectionExtensionsType, "AddSharpCoreDBProjectionHostedWorker", services, null);
    }
}

static Assembly? TryLoadOptionalAssembly(string assemblyName)
{
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
        {
            return assembly;
        }
    }

    try
    {
        return Assembly.Load(assemblyName);
    }
    catch (FileNotFoundException)
    {
        return null;
    }
}

static void InvokeProjectionExtension(Type extensionType, string methodName, params object?[] args)
{
    var method = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(candidate =>
            string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
            candidate.GetParameters().Length == args.Length);

    if (method is null)
    {
        throw new InvalidOperationException($"Projection extension method '{methodName}' with {args.Length} arguments was not found.");
    }

    _ = method.Invoke(null, args);
}
