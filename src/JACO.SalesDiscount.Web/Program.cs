using JACO.SalesDiscount.Web.Data;
using JACO.SalesDiscount.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<SalesDiscountDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient<ApprovalApiClient>();
builder.Services.AddScoped<SalesDiscountLookupService>();
builder.Services.AddSingleton<SalesDiscountAttachmentStorage>();
builder.Services.AddHttpClient();

// Shared SSO: trusts the login cookie issued by JACO Portal -- same key ring + same
// cookie name across Portal/CR/Approval/Sales Discount is what makes this work without
// a shared database. Mirrors JACO-CR's Program.cs exactly.
var keyRingPath = builder.Configuration["SharedAuth:KeyRingPath"] ?? @"C:\JACO\_shared\dpkeys";
Directory.CreateDirectory(keyRingPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("JACO-Platform");

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
