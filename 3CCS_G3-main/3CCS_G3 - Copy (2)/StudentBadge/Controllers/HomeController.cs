using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StudentBadge.Models;
using Microsoft.Data.SqlClient;

namespace WebApplication1.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;

    public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        // Check if user is logged in
        string userId = HttpContext.Session.GetString("UserId");
        string role = HttpContext.Session.GetString("Role");
        
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(role))
        {
            // Redirect to appropriate dashboard based on role
            return role switch
            {
                "student" => RedirectToAction("StudentDashboard", "Dashboard"),
                "employer" => RedirectToAction("EmployerDashboard", "Dashboard"),
                "teacher" => RedirectToAction("Dashboard", "Teacher"),
                "admin" => RedirectToAction("AdminDashboard", "Dashboard"),
                _ => RedirectToAction("Login")
            };
        }
        
        // If not logged in, redirect to login page
        return RedirectToAction("Login");
    }

    public IActionResult Privacy()
    {
        return View();
    }
    public IActionResult Login()
    {
        // Check if we need to display the imported user message
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            connection.Open();
            
            // Check if NeedsPasswordChange column exists and has any users with it set
            string query = @"
                IF EXISTS (
                    SELECT 1 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'Users' 
                    AND COLUMN_NAME = 'NeedsPasswordChange'
                )
                BEGIN
                    SELECT COUNT(*) 
                    FROM Users 
                    WHERE NeedsPasswordChange = 1
                END
                ELSE
                BEGIN
                    SELECT 0
                END";
                
            using (var command = new SqlCommand(query, connection))
            {
                int count = (int)command.ExecuteScalar();
                if (count > 0)
                {
                    TempData["ImportedUser"] = "true";
                }
            }
        }
        
        return View();
    }

    public IActionResult Signup()
    {
        return View();
    }

    public IActionResult StudentDashboard()
    {
        return View();
    }

    public IActionResult AdminDashboard()
    {
        return View();
    }


    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

