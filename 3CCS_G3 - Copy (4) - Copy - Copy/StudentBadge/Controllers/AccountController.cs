using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;

public class AccountController : Controller
{
    private readonly IConfiguration _configuration;

    public AccountController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> Signup(string fullName, string username, string password, string role, string company = "", string email = "", string phoneNumber = "")
    {
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            TempData["Error"] = "All fields are required";
            return RedirectToAction("Signup", "Home");
        }

        // Force role to be employer regardless of what was passed
        role = "employer";

        // Always validate employer fields
        if (string.IsNullOrEmpty(company) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(phoneNumber))
        {
            TempData["Error"] = "Company name, email, and phone number are required";
            return RedirectToAction("Signup", "Home");
        }

        // Generate employerId for new employer accounts
        string employerId = $"EMP{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";

        // Use connection string from configuration
        string connectionString = _configuration.GetConnectionString("DefaultConnection");
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            // Check if username already exists
            string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
            using (var command = new SqlCommand(checkUsernameQuery, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                int usernameExists = (int)await command.ExecuteScalarAsync();
                if (usernameExists > 0)
                {
                    TempData["Error"] = "Username already exists";
                    return RedirectToAction("Signup", "Home");
                }
            }

            // Generate user ID based on role
            string userId = employerId;

            // Start a transaction
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    // Check if Users table exists
                    bool usersTableExists = false;
                    string checkUsersTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users'";
                    using (var tableCommand = new SqlCommand(checkUsersTableQuery, connection, transaction))
                    {
                        int tableCount = (int)await tableCommand.ExecuteScalarAsync();
                        usersTableExists = tableCount > 0;
                    }

                    // Create Users table if it doesn't exist
                    if (!usersTableExists)
                    {
                        string createUsersTableQuery = @"
                            CREATE TABLE Users (
                                UserId NVARCHAR(50) PRIMARY KEY,
                                Username NVARCHAR(100) NOT NULL UNIQUE,
                                Password NVARCHAR(100) NOT NULL,
                                FullName NVARCHAR(100) NOT NULL,
                                Role NVARCHAR(20) NOT NULL,
                                IsActive BIT DEFAULT 1,
                                IsVerified BIT DEFAULT 0,
                                CreatedAt DATETIME DEFAULT GETDATE(),
                                LastLoginAt DATETIME NULL
                            )";
                        
                        using (var createCommand = new SqlCommand(createUsersTableQuery, connection, transaction))
                        {
                            await createCommand.ExecuteNonQueryAsync();
                        }
                    }

                    // Check if Employers table exists
                    bool employersTableExists = false;
                    string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Employers'";
                    using (var tableCommand = new SqlCommand(checkTableQuery, connection, transaction))
                    {
                        int tableCount = (int)await tableCommand.ExecuteScalarAsync();
                        employersTableExists = tableCount > 0;
                    }

                    // Create Employers table if it doesn't exist
                    if (!employersTableExists)
                    {
                        string createTableQuery = @"
                            CREATE TABLE Employers (
                                EmployerId NVARCHAR(50) PRIMARY KEY,
                                Username NVARCHAR(100) NOT NULL UNIQUE,
                                Password NVARCHAR(100) NOT NULL,
                                FullName NVARCHAR(100) NOT NULL,
                                Company NVARCHAR(100) NOT NULL,
                                Email NVARCHAR(100),
                                PhoneNumber NVARCHAR(20),
                                Address NVARCHAR(200),
                                Description NVARCHAR(MAX),
                                ProfilePicturePath NVARCHAR(255),
                                IsActive BIT DEFAULT 1,
                                CreatedAt DATETIME DEFAULT GETDATE()
                            )";
                        
                        using (var createCommand = new SqlCommand(createTableQuery, connection, transaction))
                        {
                            await createCommand.ExecuteNonQueryAsync();
                        }
                    }

                    // Insert into Users table
                    string insertUserQuery = @"
                        INSERT INTO Users (UserId, Username, Password, FullName, Role, IsVerified)
                        VALUES (@UserId, @Username, @Password, @FullName, @Role, 0)";

                    using (var command = new SqlCommand(insertUserQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Password", password);
                        command.Parameters.AddWithValue("@FullName", fullName);
                        command.Parameters.AddWithValue("@Role", role);
                        await command.ExecuteNonQueryAsync();
                    }

                    // Insert into Employers table
                    string insertEmployerQuery = @"
                        INSERT INTO Employers (EmployerId, Username, Password, FullName, Company, Email, PhoneNumber)
                        VALUES (@EmployerId, @Username, @Password, @FullName, @Company, @Email, @PhoneNumber)";

                    using (var command = new SqlCommand(insertEmployerQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Password", password);
                        command.Parameters.AddWithValue("@FullName", fullName);
                        command.Parameters.AddWithValue("@Company", company);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                        await command.ExecuteNonQueryAsync();
                    }

                    // Commit the transaction
                    transaction.Commit();

                    TempData["Success"] = "Account created successfully! Please verify your account to continue.";
                    return RedirectToAction("Login", "Home");
                }
                catch (Exception ex)
                {
                    // Roll back the transaction if something went wrong
                    transaction.Rollback();
                    TempData["Error"] = "Error creating account: " + ex.Message;
                    return RedirectToAction("Signup", "Home");
                }
            }
        }
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ViewBag.Error = "Please enter both username and password.";
            return View("~/Views/Home/Login.cshtml");
        }

        string role = null;
        string userId = null;
        string fullName = null;
        bool isActive = true;
        bool isVerified = true;
        Dictionary<string, string> userData = new Dictionary<string, string>();

        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();

            // Check if IsVerified column exists in Users table
            bool isVerifiedColumnExists = false;
            string schemaQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'IsVerified'";
            using (var schemaCommand = new SqlCommand(schemaQuery, connection))
            {
                int columnCount = (int)await schemaCommand.ExecuteScalarAsync();
                isVerifiedColumnExists = columnCount > 0;
            }

            // Update the query to include IsVerified if it exists
            string query = @"
                SELECT u.UserId, u.Username, u.FullName, u.Role, u.IsActive";
            
            if (isVerifiedColumnExists)
            {
                query += ", u.IsVerified";
            }
            
            query += @",
                       sd.IdNumber, sd.Course, sd.Section, sd.Score, sd.Achievements, sd.Comments, sd.BadgeColor,
                       td.Department, td.Position,
                       ed.Company
                FROM Users u
                LEFT JOIN StudentDetails sd ON u.UserId = sd.UserId
                LEFT JOIN TeacherDetails td ON u.UserId = td.UserId
                LEFT JOIN EmployerDetails ed ON u.UserId = ed.UserId
                WHERE u.Username = @Username AND u.Password = @Password";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@Password", password);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
                        if (!isActive)
                        {
                            ViewBag.Error = "Your account is inactive. Please contact support.";
                            return View("~/Views/Home/Login.cshtml");
                        }

                        role = reader["Role"].ToString();
                        userId = reader["UserId"].ToString();
                        fullName = reader["FullName"].ToString();

                        // Check if account is verified
                        if (isVerifiedColumnExists)
                        {
                            isVerified = reader.GetBoolean(reader.GetOrdinal("IsVerified"));
                        }

                        // Store all the data we need from the reader
                        switch (role)
                        {
                            case "student":
                                userData["IdNumber"] = reader["IdNumber"].ToString();
                                userData["Course"] = reader["Course"].ToString();
                                userData["Section"] = reader["Section"].ToString();
                                userData["Score"] = reader["Score"].ToString();
                                userData["Achievements"] = reader["Achievements"].ToString();
                                userData["Comments"] = reader["Comments"].ToString();
                                userData["BadgeColor"] = reader["BadgeColor"].ToString();
                                break;

                            case "teacher":
                                userData["Department"] = reader["Department"].ToString();
                                userData["Position"] = reader["Position"].ToString();
                                break;

                            case "employer":
                                userData["Company"] = reader["Company"].ToString();
                                break;
                        }
                    }
                    else
                    {
                        ViewBag.Error = "Invalid username or password.";
                        return View("~/Views/Home/Login.cshtml");
                    }
                }

                // Now the reader is closed, we can execute another command
                if (userId != null)
                {
                    // Update last login time
                    string updateLoginQuery = "UPDATE Users SET LastLoginAt = GETDATE() WHERE UserId = @UserId";
                    using (var updateCommand = new SqlCommand(updateLoginQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@UserId", userId);
                        await updateCommand.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        // If we get here, we have valid user data
        if (userId != null)
        {
            // Set temporary session variables for verification
            HttpContext.Session.SetString("TempUserId", userId);
            HttpContext.Session.SetString("TempUsername", username);
            HttpContext.Session.SetString("TempFullName", fullName);
            HttpContext.Session.SetString("TempRole", role);
            
            // If the account needs verification, redirect to the verification page
            if (!isVerified && role != "admin")
            {
                foreach (var key in userData.Keys)
                {
                    HttpContext.Session.SetString("Temp" + key, userData[key]);
                }
                
                return RedirectToAction("VerifyAccount");
            }
            
            // Set permanent session variables
            HttpContext.Session.SetString("UserId", userId);
            HttpContext.Session.SetString("Username", username);
            HttpContext.Session.SetString("FullName", fullName);
            HttpContext.Session.SetString("Role", role);

            // Set role-specific session variables
            switch (role)
            {
                case "student":
                    HttpContext.Session.SetString("IdNumber", userData["IdNumber"]);
                    HttpContext.Session.SetString("Course", userData["Course"]);
                    HttpContext.Session.SetString("Section", userData["Section"]);
                    HttpContext.Session.SetString("Score", userData["Score"]);
                    HttpContext.Session.SetString("Achievements", userData["Achievements"]);
                    HttpContext.Session.SetString("Comments", userData["Comments"]);
                    HttpContext.Session.SetString("BadgeColor", userData["BadgeColor"]);
                    break;

                case "teacher":
                    HttpContext.Session.SetString("TeacherId", userId);
                    HttpContext.Session.SetString("Department", userData["Department"]);
                    HttpContext.Session.SetString("Position", userData["Position"]);
                    break;

                case "employer":
                    HttpContext.Session.SetString("EmployerId", userId);
                    HttpContext.Session.SetString("Company", userData["Company"]);
                    break;

                case "admin":
                    HttpContext.Session.SetString("IsAdmin", "true");
                    break;
            }

            // Redirect based on role
            return role switch
            {
                "admin" => RedirectToAction("AdminDashboard", "Dashboard"),
                "student" => RedirectToAction("StudentDashboard", "Dashboard"),
                "employer" => RedirectToAction("EmployerDashboard", "Dashboard"),
                "teacher" => RedirectToAction("TeacherDashboard", "Dashboard"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        // We should never reach here due to the earlier validation, but just in case
        ViewBag.Error = "Login failed. Please try again.";
        return View("~/Views/Home/Login.cshtml");
    }

    // Account verification methods
    [HttpGet]
    public IActionResult VerifyAccount()
    {
        // Check if we have temporary session data
        string tempUserId = HttpContext.Session.GetString("TempUserId");
        if (string.IsNullOrEmpty(tempUserId))
        {
            return RedirectToAction("Login", "Home");
        }
        
        return View();
    }
    
    [HttpPost]
    public async Task<IActionResult> VerifyPin(string pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            ViewBag.Error = "Please enter a PIN.";
            return View("VerifyAccount");
        }
        
        // Get temporary session data
        string tempUserId = HttpContext.Session.GetString("TempUserId");
        string tempUsername = HttpContext.Session.GetString("TempUsername");
        string tempFullName = HttpContext.Session.GetString("TempFullName");
        string tempRole = HttpContext.Session.GetString("TempRole");
        
        if (string.IsNullOrEmpty(tempUserId))
        {
            return RedirectToAction("Login", "Home");
        }
        
        bool pinValid = false;
        
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            // Check if VerificationPINs table exists
            bool tableExists = false;
            string schemaQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VerificationPINs'";
            using (var schemaCommand = new SqlCommand(schemaQuery, connection))
            {
                int tableCount = (int)await schemaCommand.ExecuteScalarAsync();
                tableExists = tableCount > 0;
            }
            
            if (!tableExists)
            {
                ViewBag.Error = "Verification system is not set up. Please contact an administrator.";
                return View("VerifyAccount");
            }
            
            // Validate the PIN
            string query = @"
                SELECT PINId FROM VerificationPINs 
                WHERE PIN = @PIN 
                AND IsUsed = 0 
                AND ExpiryDate > GETDATE()";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@PIN", pin);
                var result = await command.ExecuteScalarAsync();
                
                if (result != null)
                {
                    int pinId = Convert.ToInt32(result);
                    
                    // Use a transaction to update both the PIN and user record
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Mark the PIN as used
                            string updatePinQuery = @"
                                UPDATE VerificationPINs
                                SET IsUsed = 1, 
                                    UsedById = @UserId,
                                    UsedAt = GETDATE()
                                WHERE PINId = @PINId";
                                
                            using (var updatePinCommand = new SqlCommand(updatePinQuery, connection, transaction))
                            {
                                updatePinCommand.Parameters.AddWithValue("@UserId", tempUserId);
                                updatePinCommand.Parameters.AddWithValue("@PINId", pinId);
                                await updatePinCommand.ExecuteNonQueryAsync();
                            }
                            
                            // Mark the user as verified
                            string updateUserQuery = @"
                                UPDATE Users
                                SET IsVerified = 1
                                WHERE UserId = @UserId";
                                
                            using (var updateUserCommand = new SqlCommand(updateUserQuery, connection, transaction))
                            {
                                updateUserCommand.Parameters.AddWithValue("@UserId", tempUserId);
                                await updateUserCommand.ExecuteNonQueryAsync();
                            }
                            
                            // Commit the transaction
                            transaction.Commit();
                            pinValid = true;
                        }
                        catch (Exception)
                        {
                            // Roll back the transaction if anything goes wrong
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
        }
        
        if (pinValid)
        {
            // Transfer temporary session variables to permanent ones
            HttpContext.Session.SetString("UserId", tempUserId);
            HttpContext.Session.SetString("Username", tempUsername);
            HttpContext.Session.SetString("FullName", tempFullName);
            HttpContext.Session.SetString("Role", tempRole);
            
            // Add role-specific session variables
            switch (tempRole)
            {
                case "student":
                    HttpContext.Session.SetString("IdNumber", HttpContext.Session.GetString("TempIdNumber"));
                    HttpContext.Session.SetString("Course", HttpContext.Session.GetString("TempCourse"));
                    HttpContext.Session.SetString("Section", HttpContext.Session.GetString("TempSection"));
                    HttpContext.Session.SetString("Score", HttpContext.Session.GetString("TempScore"));
                    HttpContext.Session.SetString("Achievements", HttpContext.Session.GetString("TempAchievements"));
                    HttpContext.Session.SetString("Comments", HttpContext.Session.GetString("TempComments"));
                    HttpContext.Session.SetString("BadgeColor", HttpContext.Session.GetString("TempBadgeColor"));
                    break;
                    
                case "teacher":
                    HttpContext.Session.SetString("TeacherId", tempUserId);
                    HttpContext.Session.SetString("Department", HttpContext.Session.GetString("TempDepartment"));
                    HttpContext.Session.SetString("Position", HttpContext.Session.GetString("TempPosition"));
                    break;
                    
                case "employer":
                    HttpContext.Session.SetString("EmployerId", tempUserId);
                    HttpContext.Session.SetString("Company", HttpContext.Session.GetString("TempCompany"));
                    break;
            }
            
            // Redirect based on role
            return tempRole switch
            {
                "student" => RedirectToAction("StudentDashboard", "Dashboard"),
                "employer" => RedirectToAction("EmployerDashboard", "Dashboard"),
                "teacher" => RedirectToAction("TeacherDashboard", "Dashboard"),
                _ => RedirectToAction("Index", "Home")
            };
        }
        else
        {
            ViewBag.Error = "Invalid or expired PIN. Please try again or contact an administrator.";
            return View("VerifyAccount");
        }
    }

    // Add Logout method
    public async Task<IActionResult> Logout()
    {
        // Clear session
        HttpContext.Session.Clear();
        
        // Sign out of authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        
        // Redirect to login page
        return RedirectToAction("Login", "Home");
    }
}



