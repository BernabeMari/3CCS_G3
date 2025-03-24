using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data.SqlClient;
using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using StudentBadge.Data;
using OfficeOpenXml;
using StudentBadge.Hubs;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // Set EPPlus license context
        OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
        
        services.AddControllersWithViews();  // Adds controllers with views support
        services.AddRazorPages();            // Adds Razor Pages support

        // Add database context
        services.AddDbContext<StudentContext>(options =>
            options.UseSqlServer(Configuration.GetConnectionString("YourConnectionString")));

        // Add session services (already done)
        services.AddDistributedMemoryCache();  // Use in-memory cache for session storage
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);  // Set session timeout duration
            options.Cookie.HttpOnly = true;  // Prevents JavaScript access to session cookies
            options.Cookie.IsEssential = true;  // Ensures cookie is always sent, even if privacy settings prevent it
        });

        // Add authentication services and configure the default scheme
        services.AddAuthentication(options => 
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "Cookies";
        })
        .AddCookie("Cookies", options =>
        {
            options.LoginPath = "/Home/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Home/AccessDenied";
        });

        // Add SignalR for real-time communication
        services.AddSignalR();

        // Add CORS to allow WebRTC connections
        services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy", builder =>
            {
                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithOrigins("https://localhost:5001");  // Update this for production
            });
        });

        // Add the connection string configuration (from appsettings.json)
        services.AddSingleton<IConfiguration>(Configuration);  // Your existing config
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();   // Enables serving of static files like CSS, JS

        // Enable CORS
        app.UseCors("CorsPolicy");

        // Add session middleware (must be called before UseAuthentication)
        app.UseSession();

        // Add authentication middleware before authorization
        app.UseAuthentication();  // Ensure this is before UseAuthorization

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Login}/{id?}");
            endpoints.MapControllerRoute(
              name: "studentdashboard",
              pattern: "{controller=Dashboard}/{action=StudentDashboard}/{id?}");
            endpoints.MapControllerRoute(
              name: "admindashboard",
              pattern: "{controller=Admin}/{action=AdminDashboard}/{id?}");
            
            // Map SignalR hub
            endpoints.MapHub<VideoCallHub>("/videoCallHub");
            
            endpoints.MapRazorPages();
        });

        app.UseStaticFiles(); // Make sure this is included

        // Create the directory if it doesn't exist
        var uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profilepictures");
        if (!Directory.Exists(uploadDirectory))
        {
            Directory.CreateDirectory(uploadDirectory);
        }
    }
}
