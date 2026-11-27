using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// Developer exception page/filter is only useful (and safe) in Development.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
}

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

var emailMode = builder.Configuration["Email:Mode"] ?? "Simulated";
if (string.Equals(emailMode, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, appdevPUPSCOUT.Services.SmtpEmailSender>();
    builder.Services.AddTransient<appdevPUPSCOUT.Services.IAppEmailService, appdevPUPSCOUT.Services.SmtpEmailSender>();
}
else
{
    // Simulated email writes messages (including OTP codes) to disk.
    // Refuse to run that mode in Production so secrets are not leaked.
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Simulated email mode is not allowed in Production. Set Email:Mode to \"Smtp\" and configure SMTP settings.");
    }

    builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, appdevPUPSCOUT.Services.SimulatedEmailSender>();
    builder.Services.AddTransient<appdevPUPSCOUT.Services.IAppEmailService, appdevPUPSCOUT.Services.SimulatedEmailSender>();
}
builder.Services.AddScoped<appdevPUPSCOUT.Services.OtpService>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.TrackingNumberGenerator>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.TicketWorkflowService>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.RoutingService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<appdevPUPSCOUT.Services.PriorityService>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.LookupCacheService>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.AuditService>();
builder.Services.AddScoped<appdevPUPSCOUT.Services.NotificationService>();
builder.Services.AddSingleton<appdevPUPSCOUT.Services.IEmailQueue, appdevPUPSCOUT.Services.EmailQueue>();
builder.Services.AddHostedService<appdevPUPSCOUT.Services.EmailSenderBackgroundService>();
builder.Services.AddHostedService<appdevPUPSCOUT.Services.OtpCleanupService>();
builder.Services.AddHostedService<appdevPUPSCOUT.Services.SuspensionCleanupService>();
builder.Services.AddHostedService<appdevPUPSCOUT.Services.AutoClosePendingVerificationService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("ReportSubmission", context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity?.Name ?? "authenticated"
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("You can submit at most 3 reports per hour. Please try again later.", cancellationToken);
    };
});

builder.Services.AddControllersWithViews();

var allowedEmailDomains = builder.Configuration.GetSection("Registration:AllowedEmailDomains").Get<string[]>() ?? Array.Empty<string>();
if (allowedEmailDomains.Length == 0)
{
    throw new InvalidOperationException("Registration:AllowedEmailDomains is required and must contain at least one domain.");
}

// Allow a full report with up to 5 photos at 5 MB each, plus a little overhead.
const long maxReportSize = 30 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxReportSize);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxReportSize;
    options.ValueCountLimit = 1024;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Only trust forwarded IP headers from configured proxies (defaults to none beyond loopback).
// When deploying behind a proxy or host, add that proxy's address to ForwardedHeaders:KnownProxies.
var forwardedOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
{
    if (System.Net.IPAddress.TryParse(proxy, out var address))
    {
        forwardedOptions.KnownProxies.Add(address);
    }
}
app.UseForwardedHeaders(forwardedOptions);

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Turn oversized uploads into a readable message instead of a connection drop.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413RequestEntityTooLarge)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status413RequestEntityTooLarge;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("The report is too large. Please upload at most 5 photos of 5 MB each.");
        }
    }
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Student", "Teacher", "ITPersonnel", "MaintenancePersonnel", "Admin" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Seeded test accounts are Development-only. Passwords come from user secrets,
    // never from source code. In Production, create the first admin manually.
    if (app.Environment.IsDevelopment())
    {
        var logger = app.Logger;

        // Warn about any seeded account that still accepts the old default password.
        foreach (var role in roles)
        {
            var email = $"{role.ToLower()}@pup.edu.ph";
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null && await userManager.CheckPasswordAsync(existing, "Passw0rd!"))
            {
                logger.LogWarning("Seeded account {Email} still accepts the old default password. Reset it manually; passwords are not changed automatically.", email);
            }
        }

        foreach (var role in roles)
        {
            var email = $"{role.ToLower()}@pup.edu.ph";
            // Per-role password wins; falls back to a shared dev password.
            var password = app.Configuration[$"SeedPasswords:{role}"]
                ?? app.Configuration["SeedUsers:Password"];

            if (string.IsNullOrWhiteSpace(password))
            {
                // No password configured in user secrets; skip creating this test user.
                logger.LogWarning("Missing seed password for {Role}: set SeedPasswords:{Role} or SeedUsers:Password in user secrets. Skipping seeded user {Email}.", role, role, email);
                continue;
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                await userManager.CreateAsync(user, password);
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }
    }

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    // Promote an existing registered account to Admin in any environment,
    // but only when no active Admin exists and Bootstrap:AdminEmail is set.
    var bootstrapEmail = app.Configuration["Bootstrap:AdminEmail"];
    if (!string.IsNullOrWhiteSpace(bootstrapEmail))
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        var activeAdmins = admins.Where(u => !u.IsDeactivated && (u.LockoutEnd is null || u.LockoutEnd <= DateTimeOffset.UtcNow)).ToList();

        if (activeAdmins.Count == 0)
        {
            var target = await userManager.FindByEmailAsync(bootstrapEmail);
            if (target is not null)
            {
                if (!target.EmailConfirmed)
                {
                    app.Logger.LogWarning("Bootstrap:AdminEmail {Email} is not email-confirmed; no promotion performed.", bootstrapEmail);
                }
                else
                {
                    await userManager.AddToRoleAsync(target, "Admin");

                    var auditService = scope.ServiceProvider.GetRequiredService<appdevPUPSCOUT.Services.AuditService>();
                    await auditService.Log(null, "BootstrapAdmin", null,
                        $"Promoted {target.Email} to Admin via Bootstrap:AdminEmail.",
                        target.Id, target.Email, "Startup");

                    app.Logger.LogWarning("Promoted {Email} to Admin via Bootstrap:AdminEmail. Remove Bootstrap:AdminEmail now.", bootstrapEmail);
                }
            }
            else
            {
                app.Logger.LogWarning("Bootstrap:AdminEmail {Email} was not found; no account was created or promoted.", bootstrapEmail);
            }
        }
    }

    // One-time safe copy of legacy photos from wwwroot/uploads to the new
    // non-public uploads folder. Old files are left in place.
    if (app.Environment.IsDevelopment())
    {
        var oldUploads = Path.Combine(app.Environment.WebRootPath, "uploads");
        var newUploads = Path.Combine(app.Environment.ContentRootPath, "uploads");
        if (Directory.Exists(oldUploads))
        {
            foreach (var oldFile in Directory.GetFiles(oldUploads, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(oldUploads, oldFile);
                var newFile = Path.Combine(newUploads, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
                if (!File.Exists(newFile))
                {
                    File.Copy(oldFile, newFile);
                }
            }
            app.Logger.LogInformation("Copied legacy uploads from wwwroot/uploads to uploads. Old files were left in place.");
        }
    }

    if (!db.Rooms.Any())
    {
        var building = new Building { Name = "Main Building" };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
        var floor = new Floor { Name = "1st Floor", BuildingId = building.Id };
        db.Floors.Add(floor);
        await db.SaveChangesAsync();
        var room = new Room { Name = "Room 101", FloorId = floor.Id, IsActive = true };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        db.Equipment.Add(new Equipment { Name = "Projector", RoomId = room.Id });
        await db.SaveChangesAsync();
    }
}

app.Run();
