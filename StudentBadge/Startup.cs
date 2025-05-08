using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using StudentBadge.Data;
using OfficeOpenXml;
using StudentBadge.Hubs;
using StudentBadge.Services;
using Microsoft.AspNetCore.DataProtection;
using StudentBadge.Utils;

namespace StudentBadge
{
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
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            // Configure Data Protection to persist keys
            var keysDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Keys");
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
                .SetDefaultKeyLifetime(TimeSpan.FromDays(30));

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
            services.AddScoped<EmailService>();
            
            // Register CertificateService
            services.AddScoped<CertificateService>(provider => 
                new CertificateService(Configuration.GetConnectionString("DefaultConnection")));

            // Register MarkedStudentsService
            services.AddScoped<MarkedStudentsService>();
            
            // Add DatabaseUtilityService
            services.AddScoped<DatabaseUtilityService>();
            
            // Register BadgeService
            services.AddScoped<BadgeService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Create the Keys directory if it doesn't exist
            var keysDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Keys");
            if (!Directory.Exists(keysDirectory))
            {
                Directory.CreateDirectory(keysDirectory);
            }

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
                  pattern: "{controller=StudentDashboard}/{action=StudentDashboard}/{id?}");
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

            // Update database schema for YearLevel column
            UpdateChallengesYearLevelColumn(app);
            
            // Create admin user with hashed password if it doesn't exist
            CreateAdminUserIfNotExists(app).GetAwaiter().GetResult();
        }

        private void UpdateChallengesYearLevelColumn(IApplicationBuilder app)
        {
            try
            {
                using (var scope = app.ApplicationServices.CreateScope())
                {
                    var connectionString = Configuration.GetConnectionString("DefaultConnection");
                    using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
                    {
                        connection.Open();
                        
                        // Check if YearLevel column exists and what its current type is
                        string checkColumnQuery = @"
                            SELECT DATA_TYPE 
                            FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = 'Challenges' 
                            AND COLUMN_NAME = 'YearLevel'";
                        
                        using (var command = new Microsoft.Data.SqlClient.SqlCommand(checkColumnQuery, connection))
                        {
                            var result = command.ExecuteScalar();
                            
                            // If column exists and is an INT, convert it to NVARCHAR
                            if (result != null && result.ToString().ToUpper() == "INT")
                            {
                                // First, we need to create a temporary column to hold string values
                                string createTempColumnQuery = @"
                                    ALTER TABLE Challenges 
                                    ADD YearLevel_Temp NVARCHAR(20)";
                                
                                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(createTempColumnQuery, connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                                
                                // Copy data from old column to new temp column, converting int to string
                                string updateDataQuery = @"
                                    UPDATE Challenges 
                                    SET YearLevel_Temp = CAST(YearLevel AS NVARCHAR(20))";
                                
                                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(updateDataQuery, connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                                
                                // Drop the old column
                                string dropOldColumnQuery = @"
                                    ALTER TABLE Challenges 
                                    DROP COLUMN YearLevel";
                                
                                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(dropOldColumnQuery, connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                                
                                // Rename temp column to original name
                                string renameColumnQuery = @"
                                    EXEC sp_rename 'Challenges.YearLevel_Temp', 'YearLevel', 'COLUMN'";
                                
                                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(renameColumnQuery, connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the application
                Console.WriteLine($"Error updating Challenges.YearLevel column: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task CreateAdminUserIfNotExists(IApplicationBuilder app)
        {
            try
            {
                using (var scope = app.ApplicationServices.CreateScope())
                {
                    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    await CreateAdminUser.CreateAdmin(configuration);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't stop application startup
                Console.WriteLine($"Error creating admin user: {ex.Message}");
            }
        }
    }
}
