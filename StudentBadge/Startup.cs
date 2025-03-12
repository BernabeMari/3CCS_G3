using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data.SqlClient;

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
        services.AddControllersWithViews();  // Adds controllers with views support
        services.AddRazorPages();            // Adds Razor Pages support

        // Add session services (already done)
        services.AddDistributedMemoryCache();  // Use in-memory cache for session storage
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);  // Set session timeout duration
            options.Cookie.HttpOnly = true;  // Prevents JavaScript access to session cookies
            options.Cookie.IsEssential = true;  // Ensures cookie is always sent, even if privacy settings prevent it
        });

        // Add authentication services and configure the default scheme
        services.AddAuthentication("Identity.Application")
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";  // Specify login path
                options.LogoutPath = "/Account/Logout"; // Specify logout path
                options.AccessDeniedPath = "/Account/AccessDenied"; // Specify access denied path
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
                pattern: "{controller=Home}/{action=Signup}/{id?}");
            endpoints.MapControllerRoute(
              name: "studentdashboard",
              pattern: "{controller=Dashboard}/{action=StudentDashboard}/{id?}");
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
