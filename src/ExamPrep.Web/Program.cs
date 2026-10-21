using Microsoft.AspNetCore.DataProtection;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var site = builder.Configuration.GetSection("Site").Get<SiteOptions>() ?? new SiteOptions();
var framed = site.FrameAncestors.Length > 0;

var conn = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("缺少連線字串 ConnectionStrings:Default");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn, sql => sql.EnableRetryOnFailure()));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<ChineseIdentityErrorDescriber>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    if (framed)
    {
        o.Cookie.SameSite = SameSiteMode.None;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
});

builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("ExamPrep");

if (site.BehindProxy)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

builder.Services.AddAntiforgery(o =>
{
    if (!framed) return;
    // 被 iframe 嵌入時，預設的 X-Frame-Options: SAMEORIGIN 會擋住頁面，改由 CSP frame-ancestors 控制
    o.SuppressXFrameOptionsHeader = true;
    o.Cookie.SameSite = SameSiteMode.None;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TaipeiDate>();
builder.Services.AddScoped<PracticeService>();
builder.Services.AddScoped<CountdownService>();
builder.Services.AddScoped<SeedDataService>();
builder.Services.AddScoped<QuestionCsvImporter>();
builder.Services.AddScoped<StatsService>();
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient<AiExplanationService>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<PaperZipService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ExamPrep/1.0 (+study site)");
});
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    // 原卷 ZIP 需向考選部下載檔案，限制每個連線來源的頻率
    o.AddPolicy("zip", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

await AppInitializer.RunAsync(app.Services, app.Configuration);

if (site.BehindProxy) app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    var ancestors = framed ? "'self' " + string.Join(' ', site.FrameAncestors) : "'self'";
    ctx.Response.Headers["Content-Security-Policy"] = $"frame-ancestors {ancestors}";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!site.BehindProxy) app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program;
