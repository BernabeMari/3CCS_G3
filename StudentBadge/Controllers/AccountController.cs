using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
        if (string.IsNullOrEmpty(username))
        {
            ViewBag.Error = "Please enter your username.";
            return View("~/Views/Home/Login.cshtml");
        }

        string role = null;
        string userId = null;
        string fullName = null;
        bool isActive = true;
        bool isVerified = true;
        bool needsPasswordChange = false;
        Dictionary<string, string> userData = new Dictionary<string, string>();

        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();

            // First check if this is a user without a password (NeedsPasswordChange)
            bool needsPasswordChangeColumnExists = false;
            string columnCheckQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'NeedsPasswordChange'";
            using (var columnCommand = new SqlCommand(columnCheckQuery, connection))
            {
                int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                needsPasswordChangeColumnExists = columnCount > 0;
            }

            if (string.IsNullOrEmpty(password) && needsPasswordChangeColumnExists)
            {
                // User is trying to login with only username - check if they exist and need password change
                string userQuery = @"
                    SELECT UserId, Username, FullName, Role, IsActive, IsVerified, NeedsPasswordChange
                    FROM Users 
                    WHERE Username = @Username AND NeedsPasswordChange = 1";
                    
                using (var command = new SqlCommand(userQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Found a user without password that needs to set one
                            userId = reader["UserId"].ToString();
                            role = reader["Role"].ToString();
                            fullName = reader["FullName"].ToString();
                            isActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
                            isVerified = reader.GetBoolean(reader.GetOrdinal("IsVerified"));
                            needsPasswordChange = true;
                            
                            // If user is inactive, don't allow login
                            if (!isActive)
                            {
                                ViewBag.Error = "Your account is inactive. Please contact support.";
                                return View("~/Views/Home/Login.cshtml");
                            }
                        }
                    }
                }
                
                // If not found in new table structure, check old tables
                if (userId == null)
                {
                    // Check Students table
                    bool studentsNeedsPasswordChangeExists = false;
                    string studentsColumnCheckQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'NeedsPasswordChange'";
                    using (var columnCommand = new SqlCommand(studentsColumnCheckQuery, connection))
                    {
                        int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                        studentsNeedsPasswordChangeExists = columnCount > 0;
                    }
                    
                    if (studentsNeedsPasswordChangeExists)
                    {
                        string studentQuery = @"
                            SELECT IdNumber, Username, FullName, Course, Section, Score, BadgeColor, Achievements, Comments
                            FROM Students
                            WHERE Username = @Username AND NeedsPasswordChange = 1";
                            
                        using (var command = new SqlCommand(studentQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Username", username);
                            
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // Found a student that needs to set password
                                    role = "student";
                                    fullName = reader["FullName"].ToString();
                                    userId = reader["IdNumber"].ToString(); // Use IdNumber as userId
                                    needsPasswordChange = true;
                                    
                                    // Store student data
                                    userData["IdNumber"] = reader["IdNumber"].ToString();
                                    userData["Course"] = reader["Course"].ToString();
                                    userData["Section"] = reader["Section"].ToString();
                                    userData["Score"] = reader["Score"].ToString();
                                    userData["Achievements"] = reader["Achievements"].ToString();
                                    userData["Comments"] = reader["Comments"].ToString();
                                    userData["BadgeColor"] = reader["BadgeColor"].ToString();
                                }
                            }
                        }
                    }
                    
                    // Check Teachers table
                    if (userId == null)
                    {
                        bool teachersNeedsPasswordChangeExists = false;
                        string teachersColumnCheckQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Teachers' AND COLUMN_NAME = 'NeedsPasswordChange'";
                        using (var columnCommand = new SqlCommand(teachersColumnCheckQuery, connection))
                        {
                            int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                            teachersNeedsPasswordChangeExists = columnCount > 0;
                        }
                        
                        if (teachersNeedsPasswordChangeExists)
                        {
                            string teacherQuery = @"
                                SELECT TeacherId, Username, FullName, Department, Position
                                FROM Teachers
                                WHERE Username = @Username AND NeedsPasswordChange = 1";
                                
                            using (var command = new SqlCommand(teacherQuery, connection))
                            {
                                command.Parameters.AddWithValue("@Username", username);
                                
                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        // Found a teacher that needs to set password
                                        role = "teacher";
                                        fullName = reader["FullName"].ToString();
                                        userId = reader["TeacherId"].ToString(); // Use TeacherId as userId
                                        needsPasswordChange = true;
                                        
                                        // Store teacher data
                                        userData["Department"] = reader["Department"].ToString();
                                        userData["Position"] = reader["Position"].ToString();
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (userId != null)
                {
                    // Set appropriate session variables for password change
                    HttpContext.Session.SetString("TempUserId", userId);
                    HttpContext.Session.SetString("TempUsername", username);
                    HttpContext.Session.SetString("TempFullName", fullName);
                    HttpContext.Session.SetString("TempRole", role);
                    
                    // For old table structure, store the additional data
                    foreach (var key in userData.Keys)
                    {
                        HttpContext.Session.SetString("Temp" + key, userData[key]);
                    }
                    
                    // Check if the user needs verification first
                    if (!isVerified && role != "admin")
                    {
                        return RedirectToAction("VerifyAccount");
                    }
                    
                    // Store passwordUserId for password change after verification
                    HttpContext.Session.SetString("PasswordChangeUserId", userId);
                    
                    // If already verified, go directly to password change
                    return RedirectToAction("ChangePassword");
                }
                else
                {
                    // If no matching user without password, require password
                    ViewBag.Error = "Please enter your password.";
                    return View("~/Views/Home/Login.cshtml");
                }
            }
            
            // Regular login with username and password
            if (string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter your password.";
                return View("~/Views/Home/Login.cshtml");
            }

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
            
            // Check if user needs verification (implemented as !isVerified)
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

            // Check if the user needs to change their password (for imported users)
            bool userNeedsPasswordChange = false;
            using (var passwordConnection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                await passwordConnection.OpenAsync();
                
                // Check if NeedsPasswordChange column exists
                string checkColumnQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'NeedsPasswordChange'";
                using (var columnCommand = new SqlCommand(checkColumnQuery, passwordConnection))
                {
                    int columnExists = (int)await columnCommand.ExecuteScalarAsync();
                    if (columnExists > 0)
                    {
                        // Check if this user needs a password change
                        string needsChangeQuery = "SELECT NeedsPasswordChange FROM Users WHERE UserId = @UserId";
                        using (var needsChangeCommand = new SqlCommand(needsChangeQuery, passwordConnection))
                        {
                            needsChangeCommand.Parameters.AddWithValue("@UserId", userId);
                            var result = await needsChangeCommand.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                userNeedsPasswordChange = Convert.ToBoolean(result);
                            }
                        }
                    }
                }
            }

            if (userNeedsPasswordChange)
            {
                // Store the user ID for the password change page
                HttpContext.Session.SetString("PasswordChangeUserId", userId);
                return RedirectToAction("ChangePassword");
            }
            
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
                "teacher" => RedirectToAction("Dashboard", "Teacher"),
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

                            // Check if NeedsPasswordChange column exists and update user record
                            string checkColumnQuery = @"
                                SELECT COUNT(*) 
                                FROM INFORMATION_SCHEMA.COLUMNS 
                                WHERE TABLE_NAME = 'Users' 
                                AND COLUMN_NAME = 'NeedsPasswordChange'";

                            using (var columnCommand = new SqlCommand(checkColumnQuery, connection, transaction))
                            {
                                int columnExists = (int)await columnCommand.ExecuteScalarAsync();
                                
                                // Check if the verified user needs to set a password (for imported users)
                                if (columnExists > 0)
                                {
                                    string checkPasswordQuery = @"
                                        SELECT NeedsPasswordChange 
                                        FROM Users 
                                        WHERE UserId = @UserId";
                                        
                                    using (var checkCommand = new SqlCommand(checkPasswordQuery, connection, transaction))
                                    {
                                        checkCommand.Parameters.AddWithValue("@UserId", tempUserId);
                                        var needsPasswordChangeResult = await checkCommand.ExecuteScalarAsync();
                                        
                                        if (needsPasswordChangeResult != null && needsPasswordChangeResult != DBNull.Value && Convert.ToBoolean(needsPasswordChangeResult))
                                        {
                                            // Store this flag for after verification is complete
                                            HttpContext.Session.SetString("NeedsPasswordChange", "true");
                                        }
                                    }
                                }
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
                    HttpContext.Session.SetString("IdNumber", HttpContext.Session.GetString("TempIdNumber") ?? "");
                    HttpContext.Session.SetString("Course", HttpContext.Session.GetString("TempCourse") ?? "");
                    HttpContext.Session.SetString("Section", HttpContext.Session.GetString("TempSection") ?? "");
                    HttpContext.Session.SetString("Score", HttpContext.Session.GetString("TempScore") ?? "0");
                    HttpContext.Session.SetString("Achievements", HttpContext.Session.GetString("TempAchievements") ?? "");
                    HttpContext.Session.SetString("Comments", HttpContext.Session.GetString("TempComments") ?? "");
                    HttpContext.Session.SetString("BadgeColor", HttpContext.Session.GetString("TempBadgeColor") ?? "green");
                    break;
                    
                case "teacher":
                    HttpContext.Session.SetString("TeacherId", tempUserId);
                    HttpContext.Session.SetString("Department", HttpContext.Session.GetString("TempDepartment") ?? "");
                    HttpContext.Session.SetString("Position", HttpContext.Session.GetString("TempPosition") ?? "");
                    break;
                    
                case "employer":
                    HttpContext.Session.SetString("EmployerId", tempUserId);
                    HttpContext.Session.SetString("Company", HttpContext.Session.GetString("TempCompany") ?? "");
                    break;
            }
            
            // Check if the user needs to change their password
            if (HttpContext.Session.GetString("NeedsPasswordChange") == "true")
            {
                // Clear the flag from session
                HttpContext.Session.Remove("NeedsPasswordChange");
                
                // Set the password change ID
                HttpContext.Session.SetString("PasswordChangeUserId", tempUserId);
                
                // Redirect to password change page
                return RedirectToAction("ChangePassword");
            }
            
            // Redirect based on role
            return tempRole switch
            {
                "student" => RedirectToAction("StudentDashboard", "Dashboard"),
                "employer" => RedirectToAction("EmployerDashboard", "Dashboard"),
                "teacher" => RedirectToAction("Dashboard", "Teacher"),
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

    // Add ChangePassword action methods after the Logout method
    [HttpGet]
    public IActionResult ChangePassword()
    {
        // Check if user is authenticated or in the middle of verification
        string userId = HttpContext.Session.GetString("UserId");
        string passwordUserId = HttpContext.Session.GetString("PasswordChangeUserId");
        
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(passwordUserId))
        {
            return RedirectToAction("Login", "Home");
        }
        
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(string newPassword, string confirmPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
        {
            ViewBag.Error = "Both fields are required.";
            return View();
        }
        
        if (newPassword != confirmPassword)
        {
            ViewBag.Error = "Passwords do not match.";
            return View();
        }
        
        // Get user ID (either from regular session or password change session)
        string userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            userId = HttpContext.Session.GetString("PasswordChangeUserId");
        }
        
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login", "Home");
        }
        
        bool success = false;
        
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            // Update password and reset NeedsPasswordChange flag
            string updateQuery = @"
                UPDATE Users
                SET Password = @Password, NeedsPasswordChange = 0
                WHERE UserId = @UserId";
                
            using (var command = new SqlCommand(updateQuery, connection))
            {
                command.Parameters.AddWithValue("@Password", newPassword);
                command.Parameters.AddWithValue("@UserId", userId);
                int rowsAffected = await command.ExecuteNonQueryAsync();
                
                success = rowsAffected > 0;
            }
            
            // If update failed, try old table structure
            if (!success)
            {
                string role = HttpContext.Session.GetString("TempRole") ?? HttpContext.Session.GetString("Role");
                
                if (role == "student")
                {
                    string idNumber = HttpContext.Session.GetString("TempIdNumber") ?? userId;
                    
                    string updateStudentQuery = @"
                        UPDATE Students
                        SET Password = @Password, NeedsPasswordChange = 0
                        WHERE IdNumber = @IdNumber";
                        
                    using (var command = new SqlCommand(updateStudentQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Password", newPassword);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        success = rowsAffected > 0;
                    }
                }
                else if (role == "teacher")
                {
                    string updateTeacherQuery = @"
                        UPDATE Teachers
                        SET Password = @Password, NeedsPasswordChange = 0
                        WHERE TeacherId = @TeacherId";
                        
                    using (var command = new SqlCommand(updateTeacherQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Password", newPassword);
                        command.Parameters.AddWithValue("@TeacherId", userId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        success = rowsAffected > 0;
                    }
                }
            }
        }
        
        if (success)
        {
            // Clear the password change flag
            HttpContext.Session.Remove("PasswordChangeUserId");
            
            // Clear all session variables
            HttpContext.Session.Remove("UserId");
            HttpContext.Session.Remove("Username");
            HttpContext.Session.Remove("FullName");
            HttpContext.Session.Remove("Role");
            HttpContext.Session.Remove("IdNumber");
            HttpContext.Session.Remove("Course");
            HttpContext.Session.Remove("Section");
            HttpContext.Session.Remove("Score");
            HttpContext.Session.Remove("Achievements");
            HttpContext.Session.Remove("Comments");
            HttpContext.Session.Remove("BadgeColor");
            HttpContext.Session.Remove("TeacherId");
            HttpContext.Session.Remove("Department");
            HttpContext.Session.Remove("Position");
            HttpContext.Session.Remove("EmployerId");
            HttpContext.Session.Remove("Company");
            
            // Clear temp session variables
            HttpContext.Session.Remove("TempUsername");
            HttpContext.Session.Remove("TempRole");
            HttpContext.Session.Remove("TempFullName");
            HttpContext.Session.Remove("TempIdNumber");
            HttpContext.Session.Remove("TempCourse");
            HttpContext.Session.Remove("TempSection");
            HttpContext.Session.Remove("TempScore");
            HttpContext.Session.Remove("TempAchievements");
            HttpContext.Session.Remove("TempComments");
            HttpContext.Session.Remove("TempBadgeColor");
            HttpContext.Session.Remove("TempDepartment");
            HttpContext.Session.Remove("TempPosition");
            HttpContext.Session.Remove("TempCompany");
            
            // Sign out to clear any authentication cookies
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            
            // Add success message
            TempData["Success"] = "Password set successfully! Please log in with your new credentials.";
            
            // Always redirect to login page
            return RedirectToAction("Login", "Home");
        }
        else
        {
            ViewBag.Error = "An error occurred while updating your password. Please try again.";
            return View();
        }
    }
}




