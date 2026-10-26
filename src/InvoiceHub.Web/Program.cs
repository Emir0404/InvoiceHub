using InvoiceHub.Web.Data;
using InvoiceHub.Web.Data.Repositories;
using InvoiceHub.Web.Domain;
using InvoiceHub.Web.Infrastructure;
using InvoiceHub.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

// Identity without its ready-made pages: sign in, sign up and change password are our own (AccountController).
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(o => { });
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/login";
    options.SlidingExpiration = true;
});
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        // No email sender in this demo, so accounts are usable straight after registration.
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services.AddControllersWithViews(options =>
    // Every POST needs a valid anti-forgery token, no need to remember [ValidateAntiForgeryToken] each time.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IInvoiceRepository, EfInvoiceRepository>();
builder.Services.AddScoped<IClientRepository, EfClientRepository>();
builder.Services.AddScoped<ICompanyProfileRepository, EfCompanyProfileRepository>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<CompanyProfileService>();
builder.Services.AddScoped<DashboardService>();

// In a container the default key ring would vanish on every restart and log everyone out.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("InvoiceHub");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

    if (app.Configuration.GetValue<bool>("SeedDemoData"))
        await DemoData.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (app.Configuration.GetValue("HttpsRedirection", true))
    app.UseHttpsRedirection();

// One fixed culture: number inputs always post "1234.50" and amounts always print the same way,
// no matter which regional settings the server runs with.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-US").AddSupportedCultures("en-US").AddSupportedUICultures("en-US"));

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Lets the integration tests start the real app in memory.
public partial class Program;
