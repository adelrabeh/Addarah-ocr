using DarahOcr.Data;
using DarahOcr.Services;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// ── Port Configuration ────────────────────────────────────────────────────────
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── Database ──────────────────────────────────────────────────────────────────
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL environment variable is required.");
var connectionString = ConvertDatabaseUrl(databaseUrl);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── Session ───────────────────────────────────────────────────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "darah.sid";
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ── IHttpContextAccessor ──────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ── HTTP Client (for Gemini AI) ───────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<DocxService>();
builder.Services.AddScoped<GeminiOcrService>();
builder.Services.AddSingleton<JobProgressService>();

// Job queue as hosted background service
builder.Services.AddSingleton<JobQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobQueueService>());

// ── Blazor ────────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// ── Security headers ──────────────────────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseStaticFiles();
app.UseSession();
app.UseAntiforgery();

// ── Minimal API endpoints ─────────────────────────────────────────────────────

// Preview file endpoint
app.MapGet("/api/preview/{id:int}", async (int id, AppDbContext db, HttpContext ctx) =>
{
    var session = ctx.Session;
    await session.LoadAsync();
    if (string.IsNullOrEmpty(session.GetString("username")))
        return Results.Unauthorized();

    var job = await db.Jobs.FindAsync(id);
    if (job == null) return Results.NotFound();

    var uploadsDir = Environment.GetEnvironmentVariable("UPLOADS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "uploads");
    var filePath = Path.Combine(uploadsDir, job.Filename);

    if (!File.Exists(filePath)) return Results.NotFound();

    var mimeType = job.FileType switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "tif" or "tiff" => "image/tiff",
        "pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    return Results.File(filePath, mimeType, enableRangeProcessing: true);
});

// Bulk ZIP export endpoint
app.MapGet("/api/export/zip", async (string ids, AppDbContext db, DocxService docx, HttpContext ctx) =>
{
    var session = ctx.Session;
    await session.LoadAsync();
    if (string.IsNullOrEmpty(session.GetString("username")))
        return Results.Unauthorized();

    var idList = ids.Split(',')
        .Select(s => int.TryParse(s.Trim(), out var n) ? (int?)n : null)
        .Where(n => n.HasValue)
        .Select(n => n!.Value)
        .Take(100)
        .ToList();

    if (!idList.Any()) return Results.BadRequest("لا توجد معرفات.");

    var jobs = await db.Jobs.Include(j => j.OcrResult)
        .Where(j => idList.Contains(j.Id) && j.OcrResult != null)
        .ToListAsync();

    if (!jobs.Any()) return Results.BadRequest("لا توجد وثائق معالجة للتصدير.");

    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var job in jobs)
        {
            var r = job.OcrResult!;
            var bytes = docx.GenerateDocx(
                job.OriginalFilename,
                r.RefinedText ?? "",
                r.ConfidenceScore,
                r.QualityLevel,
                r.CreatedAt);

            var entryName = $"{Path.GetFileNameWithoutExtension(job.OriginalFilename)}.docx";
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(bytes);
        }
    }

    ms.Position = 0;
    var zipBytes = ms.ToArray();
    var filename = $"darah_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

    return Results.File(zipBytes, "application/zip", filename);
});

// ── Blazor components ─────────────────────────────────────────────────────────
app.MapRazorComponents<DarahOcr.Components.App>()
   .AddInteractiveServerRenderMode();

// ── Database schema + seed ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await CreateTablesAsync(db);
        var seeder = scope.ServiceProvider.GetRequiredService<AuthService>();
        await seeder.SeedDefaultUsersAsync();
        logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database");
    }
}

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────
static async Task CreateTablesAsync(AppDbContext db)
{
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS dotnet_users (
            ""Id""                   SERIAL PRIMARY KEY,
            ""Username""             VARCHAR(64) NOT NULL UNIQUE,
            ""Email""                VARCHAR(256) NOT NULL,
            ""PasswordHash""         TEXT NOT NULL,
            ""Role""                 VARCHAR(20) NOT NULL DEFAULT 'user',
            ""Permissions""          TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
            ""IsActive""             BOOLEAN NOT NULL DEFAULT TRUE,
            ""FailedLoginAttempts""  INT NOT NULL DEFAULT 0,
            ""LockedUntil""          TIMESTAMPTZ,
            ""LastLoginAt""          TIMESTAMPTZ,
            ""CreatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS dotnet_jobs (
            ""Id""                   SERIAL PRIMARY KEY,
            ""UserId""               INT NOT NULL REFERENCES dotnet_users(""Id""),
            ""ProjectId""            INT,
            ""Filename""             VARCHAR(512) NOT NULL,
            ""OriginalFilename""     VARCHAR(512) NOT NULL,
            ""FileType""             VARCHAR(10) NOT NULL DEFAULT '',
            ""FileSize""             BIGINT NOT NULL DEFAULT 0,
            ""Status""               VARCHAR(30) NOT NULL DEFAULT 'pending',
            ""RetryCount""           INT NOT NULL DEFAULT 0,
            ""ErrorMessage""         VARCHAR(2000),
            ""ProcessingPage""       INT,
            ""TotalPages""           INT,
            ""ProcessingDurationMs"" BIGINT,
            ""ReviewerId""           INT,
            ""ReviewNotes""          TEXT,
            ""ReviewedAt""           TIMESTAMPTZ,
            ""ApproverId""           INT,
            ""ApproveNotes""         TEXT,
            ""ApprovedAt""           TIMESTAMPTZ,
            ""CreatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""UpdatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            ""StartedAt""            TIMESTAMPTZ,
            ""CompletedAt""          TIMESTAMPTZ
        );

        CREATE TABLE IF NOT EXISTS dotnet_ocr_results (
            ""Id""                   SERIAL PRIMARY KEY,
            ""JobId""                INT NOT NULL UNIQUE REFERENCES dotnet_jobs(""Id"") ON DELETE CASCADE,
            ""RawText""              TEXT,
            ""RefinedText""          TEXT,
            ""ConfidenceScore""      DOUBLE PRECISION NOT NULL DEFAULT 0,
            ""QualityLevel""         VARCHAR(20) NOT NULL DEFAULT 'medium',
            ""WordCount""            INT NOT NULL DEFAULT 0,
            ""PassCount""            INT NOT NULL DEFAULT 1,
            ""PageCount""            INT NOT NULL DEFAULT 1,
            ""ProcessingNotes""      TEXT,
            ""CreatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS dotnet_audit_logs (
            ""Id""                   SERIAL PRIMARY KEY,
            ""UserId""               INT REFERENCES dotnet_users(""Id"") ON DELETE SET NULL,
            ""Username""             VARCHAR(64),
            ""Action""               VARCHAR(64) NOT NULL,
            ""ResourceType""         VARCHAR(64),
            ""ResourceId""           INT,
            ""Details""              VARCHAR(2000),
            ""IpAddress""            VARCHAR(64),
            ""CreatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS dotnet_api_keys (
            ""Id""                   SERIAL PRIMARY KEY,
            ""UserId""               INT NOT NULL REFERENCES dotnet_users(""Id"") ON DELETE CASCADE,
            ""Name""                 VARCHAR(100) NOT NULL,
            ""KeyHash""              TEXT NOT NULL,
            ""Prefix""               VARCHAR(20) NOT NULL,
            ""IsActive""             BOOLEAN NOT NULL DEFAULT TRUE,
            ""LastUsedAt""           TIMESTAMPTZ,
            ""ExpiresAt""            TIMESTAMPTZ,
            ""CreatedAt""            TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
    ");
}

static string ConvertDatabaseUrl(string url)
{
    try
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var dbPort = uri.Port > 0 ? uri.Port : 5432;
        var dbName = uri.AbsolutePath.TrimStart('/');

        var query = uri.Query.TrimStart('?');
        var sslMode = "Disable";
        foreach (var part in query.Split('&'))
        {
            if (part.StartsWith("sslmode=", StringComparison.OrdinalIgnoreCase))
                sslMode = part[8..];
        }

        return $"Host={host};Port={dbPort};Database={dbName};Username={user};Password={pass};SSL Mode={sslMode};Trust Server Certificate=true";
    }
    catch
    {
        return url;
    }
}
