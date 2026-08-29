using JACO.SalesDiscount.Web.Data;
using JACO.SalesDiscount.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// ContentRootPath must be pinned explicitly -- Windows' Service Control Manager
// launches services with C:\WINDOWS\system32 as the working directory, and
// WebApplication.CreateBuilder resolves appsettings.json relative to the working
// directory unless told otherwise, so without this a service silently loads no config.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService();
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Logging.AddEventLog(settings => settings.SourceName = "JACO Sales Discount");
}

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SalesDiscountDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// Must match InternalApi:Key in JACO.Approval.Api's own config -- that's what proves this
// call is coming from a trusted JACO service, not an arbitrary client on the port.
builder.Services.AddHttpClient<ApprovalApiClient>(c =>
{
    var key = builder.Configuration["ApprovalApi:Key"];
    if (!string.IsNullOrEmpty(key)) c.DefaultRequestHeaders.Add("X-Jaco-Api-Key", key);
});
builder.Services.AddScoped<SalesDiscountLookupService>();
builder.Services.AddSingleton<SalesDiscountAttachmentStorage>();
builder.Services.AddHttpClient();

// Shared SSO: trusts the login cookie issued by JACO Portal -- same key ring + same
// cookie name across Portal/CR/Approval/Sales Discount is what makes this work without
// a shared database. Mirrors JACO-CR's Program.cs exactly.
var keyRingPath = builder.Configuration["SharedAuth:KeyRingPath"] ?? @"C:\JACO\_shared\dpkeys";
Directory.CreateDirectory(keyRingPath);
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("JACO-Platform");

// See JACO.Approval.Web/Program.cs for why -- same shared key ring, same fix, opt-in via
// SharedAuth:KeyRingCertThumbprint once a certificate is provisioned.
var keyRingCertThumbprint = builder.Configuration["SharedAuth:KeyRingCertThumbprint"];
if (!string.IsNullOrEmpty(keyRingCertThumbprint))
    dataProtectionBuilder.ProtectKeysWithCertificate(keyRingCertThumbprint);

var cookieName = builder.Configuration["SharedAuth:CookieName"] ?? ".JACO.Auth";
var portalLoginUrl = builder.Configuration["SharedAuth:PortalLoginUrl"] ?? "http://localhost:5010/Account/Login";
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            var returnUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
            ctx.Response.Redirect($"{portalLoginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SalesDiscountAdmin", p => p.RequireRole("SALESDISCOUNT_ADMIN", "PORTAL_ADMIN", "SYSTEM_ADMIN"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
