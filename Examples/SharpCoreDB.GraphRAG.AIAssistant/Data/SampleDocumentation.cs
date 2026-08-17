using SharpCoreDB.GraphRAG.AIAssistant.Models;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.GraphRAG.AIAssistant.Data;

/// <summary>
/// Loads sample technical documentation with realistic content and relationships.
/// Demonstrates how GraphRAG improves context retrieval for AI assistants.
/// </summary>
public sealed class SampleDocumentation
{
    private static readonly List<DocumentationArticle> Articles = 
    [
        // === Authentication & Security ===
        new()
        {
            Id = 1,
            Title = "JWT Authentication Guide",
            Category = "Security",
            Tags = ["authentication", "jwt", "tokens", "security"],
            DifficultyLevel = "Intermediate",
            ReadingTimeMinutes = 12,
            Url = "/docs/security/jwt-authentication",
            Content = @"JSON Web Tokens (JWT) provide a stateless authentication mechanism for modern web applications.

**Key Concepts:**
- Token-based authentication eliminates server-side session storage
- JWTs contain claims (user data) encoded in the token itself
- Tokens are signed using HMAC or RSA for integrity verification

**Implementation Steps:**
1. User submits credentials to /auth/login endpoint
2. Server validates credentials against database
3. Server generates JWT with user claims (id, roles, etc.)
4. Client stores token (localStorage or httpOnly cookie)
5. Client includes token in Authorization header for API requests
6. Server validates token signature and extracts claims

**Security Considerations:**
- Always use HTTPS to prevent token interception
- Set short expiration times (15-60 minutes)
- Implement refresh token rotation
- Store sensitive data server-side, not in JWT
- Validate token signature on every request

**Code Example:**
```csharp
var tokenHandler = new JwtSecurityTokenHandler();
var key = Encoding.ASCII.GetBytes(secretKey);
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email)
    }),
    Expires = DateTime.UtcNow.AddMinutes(30),
    SigningCredentials = new SigningCredentials(
        new SymmetricSecurityKey(key), 
        SecurityAlgorithms.HmacSha256Signature)
};
var token = tokenHandler.CreateToken(tokenDescriptor);
```"
        },

        new()
        {
            Id = 2,
            Title = "User Models and Database Schema",
            Category = "Database",
            Tags = ["database", "models", "schema", "users"],
            DifficultyLevel = "Beginner",
            ReadingTimeMinutes = 8,
            Url = "/docs/database/user-models",
            Content = @"Proper user data modeling is foundational for authentication systems.

**User Entity Design:**
```csharp
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}
```

**Database Schema:**
```sql
CREATE TABLE users (
    id GUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    salt TEXT NOT NULL,
    created_at DATETIME NOT NULL,
    last_login_at DATETIME,
    is_active BOOLEAN DEFAULT TRUE
);

CREATE INDEX idx_users_email ON users(email);
```

**Password Hashing:**
- Never store passwords in plain text
- Use bcrypt, Argon2, or PBKDF2
- Include unique salt per user
- Set work factor appropriately (10-12 for bcrypt)

**Best Practices:**
- Use UUIDs/GUIDs for primary keys (prevents enumeration attacks)
- Add soft delete (is_active flag) instead of hard deletes
- Track last_login_at for security auditing
- Normalize email addresses (lowercase, trim)"
        },

        new()
        {
            Id = 3,
            Title = "OAuth 2.0 Overview",
            Category = "Security",
            Tags = ["oauth", "authentication", "third-party", "sso"],
            DifficultyLevel = "Advanced",
            ReadingTimeMinutes = 15,
            Url = "/docs/security/oauth2",
            Content = @"OAuth 2.0 is an authorization framework for delegated access to resources.

**Grant Types:**
1. **Authorization Code** - Most secure, for server-side apps
2. **Implicit** - Deprecated, use Authorization Code with PKCE instead
3. **Client Credentials** - For machine-to-machine communication
4. **Resource Owner Password** - Only for trusted first-party apps

**Authorization Code Flow:**
1. Client redirects user to authorization server
2. User authenticates and grants permissions
3. Server redirects back with authorization code
4. Client exchanges code for access token (server-to-server)
5. Client uses access token to access protected resources

**Key Concepts:**
- **Access Token**: Short-lived token for API access (15-60 min)
- **Refresh Token**: Long-lived token to obtain new access tokens
- **Scopes**: Define granular permissions (read:profile, write:posts)
- **PKCE**: Proof Key for Code Exchange, prevents code interception

**Integration Example:**
```csharp
// Configure OAuth provider (Google, GitHub, etc.)
services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = configuration[""Google:ClientId""];
        options.ClientSecret = configuration[""Google:ClientSecret""];
        options.Scope.Add(""profile"");
        options.Scope.Add(""email"");
    });
```

**Common Providers:**
- Google: User sign-in, calendar, email
- GitHub: Developer authentication, repository access
- Microsoft: Azure AD, Office 365 integration
- Auth0: Managed identity platform"
        },

        new()
        {
            Id = 4,
            Title = "HTTP Headers and Cookie Security",
            Category = "Web",
            Tags = ["http", "headers", "cookies", "security"],
            DifficultyLevel = "Beginner",
            ReadingTimeMinutes = 6,
            Url = "/docs/web/http-headers",
            Content = @"Understanding HTTP headers is essential for web security.

**Authentication Headers:**
```http
Authorization: Bearer <jwt-token>
Cookie: sessionId=abc123; HttpOnly; Secure; SameSite=Strict
```

**Security Headers:**
- **Content-Security-Policy**: Prevent XSS attacks
- **X-Frame-Options**: Prevent clickjacking
- **Strict-Transport-Security**: Enforce HTTPS
- **X-Content-Type-Options**: Prevent MIME sniffing

**Cookie Attributes:**
- **HttpOnly**: JavaScript cannot access (prevents XSS theft)
- **Secure**: Only sent over HTTPS
- **SameSite**: Prevents CSRF attacks
  - Strict: Never send cross-site
  - Lax: Send on top-level navigation
  - None: Send everywhere (requires Secure)

**Example (ASP.NET Core):**
```csharp
Response.Cookies.Append(""authToken"", token, new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Strict,
    Expires = DateTimeOffset.UtcNow.AddHours(1)
});
```

**CORS Headers:**
```csharp
app.UseCors(policy => policy
    .WithOrigins(""https://example.com"")
    .AllowCredentials()
    .AllowAnyHeader()
    .AllowAnyMethod());
```"
        },

        new()
        {
            Id = 5,
            Title = "Role-Based Access Control (RBAC)",
            Category = "Security",
            Tags = ["rbac", "permissions", "authorization", "roles"],
            DifficultyLevel = "Intermediate",
            ReadingTimeMinutes = 10,
            Url = "/docs/security/rbac",
            Content = @"RBAC controls what authenticated users can access.

**Core Concepts:**
- **Roles**: Admin, Editor, Viewer, etc.
- **Permissions**: Create, Read, Update, Delete (CRUD)
- **Resources**: Posts, Users, Settings, etc.

**Implementation Approaches:**
1. **Role-Based**: User has roles, roles grant permissions
2. **Attribute-Based (ABAC)**: Rules based on user/resource attributes
3. **Policy-Based**: Complex rules engine

**ASP.NET Core Authorization:**
```csharp
// Configure roles
services.AddAuthorization(options =>
{
    options.AddPolicy(""RequireAdmin"", policy =>
        policy.RequireRole(""Admin""));

    options.AddPolicy(""CanEditPosts"", policy =>
        policy.RequireClaim(""Permission"", ""Posts:Edit""));
});

// Use in controllers
[Authorize(Policy = ""RequireAdmin"")]
public IActionResult DeleteUser(Guid id) { }

[Authorize(Policy = ""CanEditPosts"")]
public IActionResult UpdatePost(int id, Post post) { }
```

**Database Schema:**
```sql
CREATE TABLE roles (
    id INT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE user_roles (
    user_id GUID,
    role_id INT,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE permissions (
    id INT PRIMARY KEY,
    resource TEXT NOT NULL,
    action TEXT NOT NULL
);

CREATE TABLE role_permissions (
    role_id INT,
    permission_id INT,
    PRIMARY KEY (role_id, permission_id)
);
```

**Middleware Example:**
```csharp
app.Use(async (context, next) =>
{
    var user = context.User;
    if (user.IsInRole(""Admin"") || user.HasClaim(""Permission"", ""Posts:Read""))
    {
        await next();
    }
    else
    {
        context.Response.StatusCode = 403;
    }
});
```"
        },

        // === Database & Deployment ===
        new()
        {
            Id = 6,
            Title = "Database Connection Setup",
            Category = "Database",
            Tags = ["database", "connection", "setup", "configuration"],
            DifficultyLevel = "Beginner",
            ReadingTimeMinutes = 5,
            Url = "/docs/database/connection-setup",
            Content = @"Establishing database connections is the first step in any data-driven application.

**Connection String Format:**
```
Server=localhost;Database=myapp;User Id=sa;Password=SecurePass123;
```

**Best Practices:**
- Store connection strings in configuration files (appsettings.json)
- Never commit credentials to source control
- Use environment variables in production
- Implement connection pooling for performance

**ASP.NET Core Configuration:**
```json
{
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Server=localhost;Database=myapp;Trusted_Connection=True;""
  }
}
```

```csharp
var connectionString = configuration.GetConnectionString(""DefaultConnection"");
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
```

**Connection Pooling:**
- Reuses existing connections instead of creating new ones
- Reduces latency and resource usage
- Configured automatically in most providers
- Monitor pool size in production

**Health Checks:**
```csharp
services.AddHealthChecks()
    .AddSqlServer(connectionString, name: ""database"");
```"
        },

        new()
        {
            Id = 7,
            Title = "Docker Deployment Guide",
            Category = "Deployment",
            Tags = ["docker", "containers", "deployment", "devops"],
            DifficultyLevel = "Intermediate",
            ReadingTimeMinutes = 14,
            Url = "/docs/deployment/docker",
            Content = @"Docker containers provide consistent deployment across environments.

**Dockerfile Example:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY [""MyApp.csproj"", ""./""]]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT [""dotnet"", ""MyApp.dll""]
```

**Build and Run:**
```bash
docker build -t myapp:latest .
docker run -d -p 8080:80 --name myapp-container myapp:latest
```

**Docker Compose:**
```yaml
version: '3.8'
services:
  web:
    build: .
    ports:
      - ""8080:80""
    environment:
      - ConnectionStrings__DefaultConnection=Server=db;Database=myapp
    depends_on:
      - db

  db:
    image: postgres:15
    environment:
      - POSTGRES_PASSWORD=SecurePass123
    volumes:
      - db-data:/var/lib/postgresql/data

volumes:
  db-data:
```

**Best Practices:**
- Use multi-stage builds to reduce image size
- Run containers as non-root user
- Use .dockerignore to exclude unnecessary files
- Tag images with version numbers, not just 'latest'
- Health checks for container monitoring"
        },

        new()
        {
            Id = 8,
            Title = "API Security Best Practices",
            Category = "Security",
            Tags = ["api", "security", "rest", "best-practices"],
            DifficultyLevel = "Intermediate",
            ReadingTimeMinutes = 11,
            Url = "/docs/security/api-best-practices",
            Content = @"Securing REST APIs requires multiple layers of protection.

**Authentication & Authorization:**
- Require authentication for all endpoints (except public docs)
- Use JWT or OAuth 2.0 for stateless auth
- Implement role-based access control (RBAC)
- Validate tokens on every request

**Input Validation:**
```csharp
[HttpPost]
public IActionResult CreateUser([FromBody] CreateUserRequest request)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    // Sanitize input
    var email = request.Email.Trim().ToLowerInvariant();

    // Validate business rules
    if (_userRepository.EmailExists(email))
        return Conflict(""Email already registered"");

    // Proceed with creation
}
```

**Rate Limiting:**
- Prevent brute force attacks
- Limit API calls per user/IP
- Return 429 Too Many Requests when exceeded

```csharp
services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? ""anonymous"",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

**Security Headers:**
- X-Content-Type-Options: nosniff
- X-Frame-Options: DENY
- Content-Security-Policy
- Strict-Transport-Security (HSTS)

**HTTPS Enforcement:**
```csharp
services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});
```

**Error Handling:**
- Never expose stack traces in production
- Return generic error messages to clients
- Log detailed errors server-side
- Use consistent error response format"
        },

        new()
        {
            Id = 9,
            Title = "Token Refresh Strategies",
            Category = "Security",
            Tags = ["jwt", "refresh-tokens", "security", "authentication"],
            DifficultyLevel = "Advanced",
            ReadingTimeMinutes = 9,
            Url = "/docs/security/token-refresh",
            Content = @"Refresh tokens enable long-lived sessions without compromising security.

**Token Pair Strategy:**
- **Access Token**: Short-lived (15-60 min), includes user claims
- **Refresh Token**: Long-lived (days/weeks), used to obtain new access tokens

**Refresh Flow:**
1. Client receives access token (expires in 30 min) + refresh token
2. Client uses access token for API requests
3. When access token expires, client sends refresh token to /auth/refresh
4. Server validates refresh token, issues new access token
5. Optional: Issue new refresh token (rotation)

**Implementation:**
```csharp
[HttpPost(""refresh"")]
public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
{
    var user = await ValidateRefreshTokenAsync(request.RefreshToken);
    if (user == null)
        return Unauthorized(""Invalid refresh token"");

    // Generate new access token
    var newAccessToken = GenerateJwtToken(user);

    // Optional: Rotate refresh token
    var newRefreshToken = GenerateRefreshToken();
    await StoreRefreshTokenAsync(user.Id, newRefreshToken);
    await RevokeRefreshTokenAsync(request.RefreshToken);

    return Ok(new
    {
        AccessToken = newAccessToken,
        RefreshToken = newRefreshToken,
        ExpiresIn = 1800 // 30 minutes
    });
}
```

**Refresh Token Storage:**
```sql
CREATE TABLE refresh_tokens (
    id GUID PRIMARY KEY,
    user_id GUID NOT NULL,
    token TEXT NOT NULL UNIQUE,
    expires_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL,
    revoked_at DATETIME NULL,
    replaced_by_token TEXT NULL
);

CREATE INDEX idx_refresh_tokens_user ON refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_token ON refresh_tokens(token);
```

**Security Considerations:**
- Store refresh tokens server-side (database)
- Implement token rotation (invalidate old refresh token on use)
- Set reasonable expiration (7-30 days)
- Track token families to detect reuse attacks
- Revoke all user tokens on password change"
        },

        new()
        {
            Id = 10,
            Title = "Kubernetes Deployment",
            Category = "Deployment",
            Tags = ["kubernetes", "k8s", "orchestration", "containers"],
            DifficultyLevel = "Advanced",
            ReadingTimeMinutes = 16,
            Url = "/docs/deployment/kubernetes",
            Content = @"Kubernetes orchestrates containerized applications at scale.

**Deployment Manifest:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapp
  labels:
    app: myapp
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapp
  template:
    metadata:
      labels:
        app: myapp
    spec:
      containers:
      - name: myapp
        image: myapp:1.0.0
        ports:
        - containerPort: 80
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: db-secret
              key: connection-string
        resources:
          requests:
            memory: ""256Mi""
            cpu: ""250m""
          limits:
            memory: ""512Mi""
            cpu: ""500m""
        livenessProbe:
          httpGet:
            path: /health
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
```

**Service & Ingress:**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: myapp-service
spec:
  selector:
    app: myapp
  ports:
  - port: 80
    targetPort: 80
  type: ClusterIP
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: myapp-ingress
spec:
  rules:
  - host: myapp.example.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: myapp-service
            port:
              number: 80
```

**Key Concepts:**
- **Pod**: Smallest deployable unit (one or more containers)
- **Deployment**: Manages replica sets and rolling updates
- **Service**: Network endpoint for pods
- **Ingress**: HTTP(S) routing to services
- **ConfigMap**: Configuration data
- **Secret**: Sensitive data (passwords, tokens)

**Deployment Commands:**
```bash
kubectl apply -f deployment.yaml
kubectl get pods
kubectl logs <pod-name>
kubectl scale deployment myapp --replicas=5
kubectl rollout status deployment/myapp
```"
        }
    ];

    private static readonly List<DocumentRelationship> Relationships =
    [
        // JWT depends on User Models and HTTP Headers
        new() { SourceDocId = 1, TargetDocId = 2, RelationType = RelationshipType.DependsOn, Weight = 0.95 },
        new() { SourceDocId = 1, TargetDocId = 4, RelationType = RelationshipType.DependsOn, Weight = 0.85 },

        // JWT related to OAuth and RBAC
        new() { SourceDocId = 1, TargetDocId = 3, RelationType = RelationshipType.RelatedTo, Weight = 0.75 },
        new() { SourceDocId = 1, TargetDocId = 5, RelationType = RelationshipType.RelatedTo, Weight = 0.8 },
        new() { SourceDocId = 1, TargetDocId = 9, RelationType = RelationshipType.RelatedTo, Weight = 0.9 },

        // User Models prerequisite for JWT and RBAC
        new() { SourceDocId = 2, TargetDocId = 1, RelationType = RelationshipType.Prerequisite, Weight = 1.0 },
        new() { SourceDocId = 2, TargetDocId = 5, RelationType = RelationshipType.Prerequisite, Weight = 0.9 },
        new() { SourceDocId = 2, TargetDocId = 6, RelationType = RelationshipType.FollowsFrom, Weight = 0.7 },

        // OAuth related to JWT
        new() { SourceDocId = 3, TargetDocId = 1, RelationType = RelationshipType.RelatedTo, Weight = 0.75 },
        new() { SourceDocId = 3, TargetDocId = 4, RelationType = RelationshipType.DependsOn, Weight = 0.8 },

        // HTTP Headers prerequisite for security topics
        new() { SourceDocId = 4, TargetDocId = 1, RelationType = RelationshipType.Prerequisite, Weight = 0.85 },
        new() { SourceDocId = 4, TargetDocId = 8, RelationType = RelationshipType.Prerequisite, Weight = 0.9 },

        // RBAC depends on JWT
        new() { SourceDocId = 5, TargetDocId = 1, RelationType = RelationshipType.DependsOn, Weight = 0.85 },
        new() { SourceDocId = 5, TargetDocId = 2, RelationType = RelationshipType.DependsOn, Weight = 0.8 },

        // Database connection prerequisite for deployment
        new() { SourceDocId = 6, TargetDocId = 7, RelationType = RelationshipType.Prerequisite, Weight = 0.75 },
        new() { SourceDocId = 6, TargetDocId = 10, RelationType = RelationshipType.Prerequisite, Weight = 0.7 },

        // Docker and Kubernetes related
        new() { SourceDocId = 7, TargetDocId = 10, RelationType = RelationshipType.RelatedTo, Weight = 0.9 },
        new() { SourceDocId = 7, TargetDocId = 6, RelationType = RelationshipType.DependsOn, Weight = 0.7 },

        // API Security depends on JWT and HTTP Headers
        new() { SourceDocId = 8, TargetDocId = 1, RelationType = RelationshipType.DependsOn, Weight = 0.85 },
        new() { SourceDocId = 8, TargetDocId = 4, RelationType = RelationshipType.DependsOn, Weight = 0.9 },
        new() { SourceDocId = 8, TargetDocId = 5, RelationType = RelationshipType.RelatedTo, Weight = 0.8 },

        // Token Refresh extends JWT
        new() { SourceDocId = 9, TargetDocId = 1, RelationType = RelationshipType.FollowsFrom, Weight = 0.95 },
        new() { SourceDocId = 9, TargetDocId = 2, RelationType = RelationshipType.DependsOn, Weight = 0.75 },

        // Kubernetes follows Docker
        new() { SourceDocId = 10, TargetDocId = 7, RelationType = RelationshipType.FollowsFrom, Weight = 0.9 },
        new() { SourceDocId = 10, TargetDocId = 6, RelationType = RelationshipType.DependsOn, Weight = 0.7 }
    ];

    /// <summary>
    /// Loads all sample documentation into the database.
    /// </summary>
    public static Task LoadAsync(IDatabase db, ILogger logger, CancellationToken ct = default)
    {
        logger.LogInformation("Loading {Count} sample documentation articles...", Articles.Count);

        // Insert articles
        var insertStatements = new List<string>();

        foreach (var article in Articles)
        {
            var tagsJson = System.Text.Json.JsonSerializer.Serialize(article.Tags);
            var content = article.Content.Replace("'", "''"); // Escape quotes
            var title = article.Title.Replace("'", "''");

            insertStatements.Add($@"
                INSERT INTO documentation (Id, Title, Content, Category, Tags, Url, DifficultyLevel, ReadingTimeMinutes, CreatedAt, UpdatedAt)
                VALUES ({article.Id}, '{title}', '{content}', '{article.Category}', '{tagsJson}', 
                        '{article.Url}', '{article.DifficultyLevel}', {article.ReadingTimeMinutes},
                        '{article.CreatedAt:yyyy-MM-dd HH:mm:ss}', '{article.UpdatedAt:yyyy-MM-dd HH:mm:ss}')");
        }

        db.ExecuteBatchSQL(insertStatements);

        logger.LogInformation("Inserted {Count} articles", Articles.Count);

        // Skip mock embeddings - the demo works without them since we mock similarity scores in queries
        logger.LogInformation("Skipped mock embeddings (not needed for demo)");

        // Insert relationships
        var relationshipStatements = new List<string>();

        foreach (var rel in Relationships)
        {
            relationshipStatements.Add($@"
                INSERT INTO doc_relationships (source_id, target_id, relationship_type, weight)
                VALUES ({rel.SourceDocId}, {rel.TargetDocId}, '{rel.RelationType.ToString().ToLowerInvariant()}', {rel.Weight})");
        }

        db.ExecuteBatchSQL(relationshipStatements);

        logger.LogInformation("Inserted {Count} relationships", Relationships.Count);

        db.Flush();
        db.ForceSave();

        logger.LogInformation("✅ Sample documentation loaded successfully!");
        logger.LogInformation("   - {Articles} articles", Articles.Count);
        logger.LogInformation("   - {Relationships} relationships", Relationships.Count);
        logger.LogInformation("   - Graph depth: 2-3 hops");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets article count for verification.
    /// </summary>
    public static int GetArticleCount() => Articles.Count;

    /// <summary>
    /// Gets relationship count for verification.
    /// </summary>
    public static int GetRelationshipCount() => Relationships.Count;
}
