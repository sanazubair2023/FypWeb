using System.Security.Claims;
using System.Threading.RateLimiting;
using Fyp_Backend.Models;
using FypWeb.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<Fyp1Context>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("fyp1")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<ContractRules>();
builder.Services.AddScoped<ResignationWorkflowService>();
builder.Services.AddScoped<WorkerNotificationService>();
builder.Services.AddHostedService<ResignationExpiryWorker>();
builder.Services.AddScoped<FypWeb.Helpers.ImageService>();
builder.Services.AddHttpContextAccessor();

var keyPath = builder.Configuration["Security:DataProtectionKeys"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(keyPath);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("FypWeb");
var keyCertificateThumbprint = builder.Configuration["Security:DataProtectionCertificateThumbprint"];
if (!string.IsNullOrWhiteSpace(keyCertificateThumbprint))
{
    // The certificate must be in CurrentUser/My with its private key and be
    // readable by the application identity (useful for Linux/containers).
    dataProtection.ProtectKeysWithCertificate(keyCertificateThumbprint);
}
else if (OperatingSystem.IsWindows())
{
    // Existing unencrypted keys remain readable; newly generated keys are protected.
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.Name = "FypWeb.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnValidatePrincipal = async context =>
        {
            var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var stamp = context.Principal?.FindFirstValue("PasswordStamp");
            if (!int.TryParse(idText, out var id) || string.IsNullOrEmpty(role) || string.IsNullOrEmpty(stamp))
            {
                context.RejectPrincipal();
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<Fyp1Context>();
            string? stored = role switch
            {
                "Client" => await db.Clients.Where(x => x.ClientId == id).Select(x => x.Password).FirstOrDefaultAsync(),
                "Worker" => await db.Workers.Where(x => x.WorkerId == id).Select(x => x.Password).FirstOrDefaultAsync(),
                "Company" => await db.Companies.Where(x => x.CompanyID == id).Select(x => x.Password).FirstOrDefaultAsync(),
                "Police" => await db.PoliceOfficers.Where(x => x.PoliceID == id).Select(x => x.Password).FirstOrDefaultAsync(),
                _ => null
            };
            if (stored == null || !string.Equals(stamp, PasswordService.Stamp(stored), StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(self)";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    // The current Razor UI uses inline scripts/styles, so this is a scoped
    // policy rather than a claim of nonce-based XSS prevention. Untrusted
    // profile/filter values are still DOM-text encoded at their sources.
    var imageSources = app.Environment.IsDevelopment() ? "https: http:" : "https:";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://unpkg.com; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://unpkg.com; " +
        "font-src 'self' data: https://cdn.jsdelivr.net; " +
        $"img-src 'self' data: blob: {imageSources}; " +
        "connect-src 'self' https://photon.komoot.io; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
