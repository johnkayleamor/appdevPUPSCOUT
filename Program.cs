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
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

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

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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

    foreach (var role in roles)
    {
        var email = $"{role.ToLower()}@pup.edu.ph";
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            await userManager.CreateAsync(user, "Passw0rd!");
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
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
