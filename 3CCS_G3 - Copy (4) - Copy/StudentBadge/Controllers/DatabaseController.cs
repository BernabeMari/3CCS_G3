using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Text;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace StudentBadge.Controllers
{
    public class DatabaseController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public DatabaseController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Migrate()
        {
            var results = new StringBuilder();
            
            try
            {
                string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "DatabaseMigration.sql");
                
                if (!System.IO.File.Exists(scriptPath))
                {
                    // If the file doesn't exist in the SQL folder, try a different location
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "StudentBadge", "SQL", "DatabaseMigration.sql");
                    
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return View("Error", "Migration script not found. Please make sure the DatabaseMigration.sql file exists.");
                    }
                }
                
                string script = await System.IO.File.ReadAllTextAsync(scriptPath);
                
                // Split the script by GO statements
                string[] batches = script.Split(new[] { "GO", "go" }, StringSplitOptions.RemoveEmptyEntries);
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    foreach (string batch in batches)
                    {
                        if (!string.IsNullOrWhiteSpace(batch))
                        {
                            using (var command = new SqlCommand(batch, connection))
                            {
                                try
                                {
                                    await command.ExecuteNonQueryAsync();
                                    // Capture PRINT messages
                                    results.AppendLine($"Executed: {batch.Substring(0, Math.Min(50, batch.Length))}...");
                                }
                                catch (Exception ex)
                                {
                                    results.AppendLine($"Error: {ex.Message} in batch: {batch.Substring(0, Math.Min(50, batch.Length))}...");
                                }
                            }
                        }
                    }
                }
                
                ViewBag.Results = results.ToString();
                ViewBag.Success = true;
                return View("MigrationResults");
            }
            catch (Exception ex)
            {
                ViewBag.Results = $"Migration error: {ex.Message}";
                ViewBag.Success = false;
                return View("MigrationResults");
            }
        }
    }
} 