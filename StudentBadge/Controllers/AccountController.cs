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
using System.Net;
using System.Net.Mail;
using System.Text;
using StudentBadge.Services;
using System;

public class AccountController : Controller
{
    private readonly IConfiguration _configuration;
    private static Dictionary<string, int> _failedLoginAttempts = new Dictionary<string, int>();
    private static Dictionary<string, DateTime> _lockoutTimes = new Dictionary<string, DateTime>();

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

        // Validate password meets minimum requirements
        if (password.Length < 8 || !password.Any(char.IsUpper) || !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            TempData["Error"] = "Password must be at least 8 characters long, contain at least one uppercase letter, and one symbol";
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

        // Hash the password
        string hashedPassword = PasswordHashService.HashPassword(password);

        // Generate employerId for new employer accounts
        string employerId = $"EMP{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";

        // Use connection string from configuration
        string connectionString = _configuration.GetConnectionString("DefaultConnection");
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();

            // Check if username already exists in Users table
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

            // Also check if username exists in Employers table (for legacy support)
            string checkEmployersQuery = "IF OBJECT_ID('Employers', 'U') IS NOT NULL SELECT COUNT(*) FROM Employers WHERE Username = @Username ELSE SELECT 0";
            using (var command = new SqlCommand(checkEmployersQuery, connection))
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
                                Email NVARCHAR(100),
                                PhoneNumber NVARCHAR(20),
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
                    else
                    {
                        // Check if Email and PhoneNumber columns exist in Users table
                        bool emailColumnExists = false;
                        string checkEmailColumnQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'Email'";
                        using (var columnCommand = new SqlCommand(checkEmailColumnQuery, connection, transaction))
                        {
                            int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                            emailColumnExists = columnCount > 0;
                        }

                        bool phoneColumnExists = false;
                        string checkPhoneColumnQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'PhoneNumber'";
                        using (var columnCommand = new SqlCommand(checkPhoneColumnQuery, connection, transaction))
                        {
                            int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                            phoneColumnExists = columnCount > 0;
                        }

                        // Add Email column if it doesn't exist
                        if (!emailColumnExists)
                        {
                            string addEmailColumnQuery = "ALTER TABLE Users ADD Email NVARCHAR(100)";
                            using (var alterCommand = new SqlCommand(addEmailColumnQuery, connection, transaction))
                            {
                                await alterCommand.ExecuteNonQueryAsync();
                            }
                        }

                        // Add PhoneNumber column if it doesn't exist
                        if (!phoneColumnExists)
                        {
                            string addPhoneColumnQuery = "ALTER TABLE Users ADD PhoneNumber NVARCHAR(20)";
                            using (var alterCommand = new SqlCommand(addPhoneColumnQuery, connection, transaction))
                            {
                                await alterCommand.ExecuteNonQueryAsync();
                            }
                        }
                    }

                    // Check if EmployerDetails table exists (unified schema)
                    bool employerDetailsExists = false;
                    string checkEmployerDetailsQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'";
                    using (var tableCommand = new SqlCommand(checkEmployerDetailsQuery, connection, transaction))
                    {
                        int tableCount = (int)await tableCommand.ExecuteScalarAsync();
                        employerDetailsExists = tableCount > 0;
                    }

                    // Insert into Users table
                    string insertUserQuery = @"
                        INSERT INTO Users (UserId, Username, Password, FullName, Role, Email, PhoneNumber, IsVerified)
                        VALUES (@UserId, @Username, @Password, @FullName, @Role, @Email, @PhoneNumber, 0)";

                    using (var command = new SqlCommand(insertUserQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Password", hashedPassword);
                        command.Parameters.AddWithValue("@FullName", fullName);
                        command.Parameters.AddWithValue("@Role", role);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                        await command.ExecuteNonQueryAsync();
                    }

                    // If using the unified schema, insert into EmployerDetails
                    if (employerDetailsExists)
                    {
                        // Check for Email and PhoneNumber columns in EmployerDetails
                        bool emailColumnExists = false;
                        string checkEmailColumnQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EmployerDetails' AND COLUMN_NAME = 'Email'";
                        using (var columnCommand = new SqlCommand(checkEmailColumnQuery, connection, transaction))
                        {
                            int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                            emailColumnExists = columnCount > 0;
                        }

                        bool phoneColumnExists = false;
                        string checkPhoneColumnQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'EmployerDetails' AND COLUMN_NAME = 'PhoneNumber'";
                        using (var columnCommand = new SqlCommand(checkPhoneColumnQuery, connection, transaction))
                        {
                            int columnCount = (int)await columnCommand.ExecuteScalarAsync();
                            phoneColumnExists = columnCount > 0;
                        }

                        // Prepare the query based on available columns
                        string insertEmployerDetailsQuery = "INSERT INTO EmployerDetails (UserId, Company";
                        if (emailColumnExists) insertEmployerDetailsQuery += ", Email";
                        if (phoneColumnExists) insertEmployerDetailsQuery += ", PhoneNumber";
                        insertEmployerDetailsQuery += ") VALUES (@UserId, @Company";
                        if (emailColumnExists) insertEmployerDetailsQuery += ", @Email";
                        if (phoneColumnExists) insertEmployerDetailsQuery += ", @PhoneNumber";
                        insertEmployerDetailsQuery += ")";

                        using (var command = new SqlCommand(insertEmployerDetailsQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            command.Parameters.AddWithValue("@Company", company);
                            if (emailColumnExists) command.Parameters.AddWithValue("@Email", email);
                            if (phoneColumnExists) command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Check if legacy Employers table exists - only insert if it does and we're not using unified schema
                        bool employersTableExists = false;
                        string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Employers'";
                        using (var tableCommand = new SqlCommand(checkTableQuery, connection, transaction))
                        {
                            int tableCount = (int)await tableCommand.ExecuteScalarAsync();
                            employersTableExists = tableCount > 0;
                        }

                        // Insert into Employers table (legacy support)
                        if (employersTableExists) 
                        {
                            string insertEmployerQuery = @"
                                INSERT INTO Employers (EmployerId, Username, Password, FullName, Company, Email, PhoneNumber)
                                VALUES (@EmployerId, @Username, @Password, @FullName, @Company, @Email, @PhoneNumber)";

                            using (var command = new SqlCommand(insertEmployerQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@EmployerId", employerId);
                                command.Parameters.AddWithValue("@Username", username);
                                command.Parameters.AddWithValue("@Password", hashedPassword);
                                command.Parameters.AddWithValue("@FullName", fullName);
                                command.Parameters.AddWithValue("@Company", company);
                                command.Parameters.AddWithValue("@Email", email);
                                command.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                                await command.ExecuteNonQueryAsync();
                            }
                        }
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

        // Check if the user is currently locked out
        string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string lockoutKey = $"{username}_{ipAddress}";
        
        if (_lockoutTimes.ContainsKey(lockoutKey))
        {
            DateTime lockoutEndTime = _lockoutTimes[lockoutKey];
            if (DateTime.Now < lockoutEndTime)
            {
                int remainingSeconds = (int)(lockoutEndTime - DateTime.Now).TotalSeconds;
                ViewBag.Error = "Too many failed login attempts. Please wait before trying again.";
                ViewBag.LockoutSeconds = remainingSeconds;
                return View("~/Views/Home/Login.cshtml");
            }
            else
            {
                // Lockout period has ended, remove from dictionary
                _lockoutTimes.Remove(lockoutKey);
                _failedLoginAttempts.Remove(lockoutKey);
            }
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
                       ed.Company,
                       u.Password
                FROM Users u
                LEFT JOIN StudentDetails sd ON u.UserId = sd.UserId
                LEFT JOIN TeacherDetails td ON u.UserId = td.UserId
                LEFT JOIN EmployerDetails ed ON u.UserId = ed.UserId
                WHERE u.Username = @Username";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        // Get the stored password hash for verification
                        string storedPassword = reader["Password"].ToString();
                        
                        // Verify the provided password against the stored hash
                        bool passwordValid = PasswordHashService.VerifyPassword(storedPassword, password);
                        
                        if (!passwordValid)
                        {
                            // Increment failed login attempts if password is invalid
                            if (!_failedLoginAttempts.ContainsKey(lockoutKey))
                            {
                                _failedLoginAttempts[lockoutKey] = 1;
                            }
                            else
                            {
                                _failedLoginAttempts[lockoutKey]++;
                            }
                            
                            // Lock out after 5 failed attempts
                            if (_failedLoginAttempts[lockoutKey] >= 5)
                            {
                                // Lock out for 15 minutes
                                _lockoutTimes[lockoutKey] = DateTime.Now.AddMinutes(15);
                                ViewBag.Error = "Too many failed login attempts. Your account is temporarily locked.";
                                ViewBag.LockoutSeconds = 900; // 15 minutes in seconds
                                return View("~/Views/Home/Login.cshtml");
                            }
                            
                            ViewBag.Error = "Invalid username or password.";
                            return View("~/Views/Home/Login.cshtml");
                        }
                        
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
            // Reset failed login attempts on successful login
            if (_failedLoginAttempts.ContainsKey(lockoutKey))
            {
                _failedLoginAttempts.Remove(lockoutKey);
            }
            
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
                        string needsChangeQuery = "SELECT ISNULL(NeedsPasswordChange, 0) FROM Users WHERE UserId = @UserId";
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
        else
        {
            // Track failed login attempt
            if (!_failedLoginAttempts.ContainsKey(lockoutKey))
            {
                _failedLoginAttempts[lockoutKey] = 1;
            }
            else
            {
                _failedLoginAttempts[lockoutKey]++;
            }

            // Check if max attempts reached (3 attempts)
            if (_failedLoginAttempts[lockoutKey] >= 2)
            {
                // Set lockout end time
                DateTime lockoutEndTime = DateTime.Now.AddSeconds(30);
                
                // Lock account for 30 seconds
                _lockoutTimes[lockoutKey] = lockoutEndTime;
                
                // Also store in session for better persistence
                HttpContext.Session.SetString("Lockout_EndTime", lockoutEndTime.ToString("o"));
                
                // Clear attempts counter (will be reset after lockout)
                _failedLoginAttempts[lockoutKey] = 0;
                
                // Show lockout message with countdown
                ViewBag.Error = "Too many failed login attempts. Please wait before trying again.";
                ViewBag.LockoutSeconds = 30;
                return View("~/Views/Home/Login.cshtml");
            }

            ViewBag.Error = "Invalid username or password.";
            
            // Add a parameter to the URL to indicate a failed login for client-side tracking
            TempData["LoginFailed"] = true;
            
            return View("~/Views/Home/Login.cshtml");
        }
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
        
        // Validate password meets minimum requirements
        if (newPassword.Length < 8 || !newPassword.Any(char.IsUpper) || !newPassword.Any(c => !char.IsLetterOrDigit(c)))
        {
            ViewBag.Error = "Password must be at least 8 characters long, contain at least one uppercase letter, and one symbol";
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
            
            // Hash the new password
            string hashedPassword = PasswordHashService.HashPassword(newPassword);
            
            // Update password and reset NeedsPasswordChange flag
            string updateQuery = "UPDATE Users SET Password = @Password, NeedsPasswordChange = 0 WHERE UserId = @UserId";
            using (var updateCommand = new SqlCommand(updateQuery, connection))
            {
                updateCommand.Parameters.AddWithValue("@Password", hashedPassword);
                updateCommand.Parameters.AddWithValue("@UserId", userId);
                int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                
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
                        command.Parameters.AddWithValue("@Password", hashedPassword);
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
                        command.Parameters.AddWithValue("@Password", hashedPassword);
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

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }
    
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(string username)
    {
        if (string.IsNullOrEmpty(username))
        {
            ViewBag.Error = "Username is required";
            return View();
        }
        
        // Check if user exists and get their email
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            string query = @"
                SELECT UserId, Username, FullName, Email
                FROM Users
                WHERE Username = @Username";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        // User found, generate reset code
                        string userId = reader["UserId"].ToString();
                        string fullName = reader["FullName"].ToString();
                        string email = reader["Email"].ToString();
                        
                        if (string.IsNullOrEmpty(email))
                        {
                            ViewBag.Error = "No email address associated with this account. Please contact support.";
                            return View();
                        }
                        
                        // Generate reset code
                        string resetCode = GenerateResetCode();
                        
                        // Store reset code in database
                        await StoreResetCode(userId, resetCode);
                        
                        // Send email with verification code
                        bool emailSent = await SendVerificationEmail(email, fullName, resetCode);
                        
                        if (emailSent)
                        {
                            ViewBag.Success = $"Verification code sent to your email address";
                            
                            // Only show the email in the UI, not the code itself
                            ViewBag.DevelopmentEmail = email;
                            
                            // Save the code in a variable accessible by the server but not shown to the user
                            TempData["ResetUsername"] = username;
                            
                            return View();
                        }
                        else
                        {
                            ViewBag.Error = "Failed to send verification email. Please try again later.";
                            return View();
                        }
                    }
                    else
                    {
                        ViewBag.Error = "Username not found";
                        return View();
                    }
                }
            }
        }
    }
    
    private async Task<bool> SendVerificationEmail(string toEmail, string fullName, string resetCode)
    {
        try
        {
            // Get email settings from configuration
            string smtpServer = _configuration["EmailSettings:SmtpServer"];
            int smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"]);
            string smtpUsername = _configuration["EmailSettings:Username"];
            string smtpPassword = _configuration["EmailSettings:Password"];
            string fromEmail = _configuration["EmailSettings:FromEmail"];
            string fromName = _configuration["EmailSettings:FromName"];
            
            // Create email message
            MailMessage mail = new MailMessage();
            mail.From = new MailAddress(fromEmail, fromName);
            mail.To.Add(new MailAddress(toEmail));
            mail.Subject = "EduBadge Password Reset Verification Code";
            mail.IsBodyHtml = true;
            
            // Create email body with HTML
            string emailBody = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #e74c3c; color: white; padding: 10px 20px; text-align: center; }}
                    .content {{ padding: 20px; background-color: #fff; border: 1px solid #ddd; }}
                    .code {{ font-size: 24px; font-weight: bold; text-align: center; padding: 15px; 
                             background-color: #f9f9f9; border: 1px dashed #ccc; margin: 20px 0; letter-spacing: 5px; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #777; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h2>EduBadge Password Reset</h2>
                    </div>
                    <div class='content'>
                        <p>Hello {fullName},</p>
                        <p>We received a request to reset your password. Please use the following verification code to complete the process:</p>
                        <div class='code'>{resetCode}</div>
                        <p>This code will expire in 60 minutes.</p>
                        <p>If you did not request a password reset, please ignore this email or contact support if you have concerns.</p>
                        <p>Thank you,<br>The EduBadge Team</p>
                    </div>
                    <div class='footer'>
                        <p>This is an automated message, please do not reply to this email.</p>
                    </div>
                </div>
            </body>
            </html>";
            
            mail.Body = emailBody;
            
            // Setup SMTP client
            using (SmtpClient smtp = new SmtpClient(smtpServer, smtpPort))
            {
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
                smtp.EnableSsl = true;
                
                // Send email
                await smtp.SendMailAsync(mail);
                return true;
            }
        }
        catch (Exception ex)
        {
            // Log the error (in a production app)
            Console.WriteLine($"Email sending error: {ex.Message}");
            return false;
        }
    }
    
    [HttpGet]
    public IActionResult VerifyResetCode()
    {
        string username = TempData["ResetUsername"] as string;
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToAction("ForgotPassword");
        }
        
        // Keep username for the next request
        TempData["ResetUsername"] = username;
        
        return View();
    }
    
    [HttpPost]
    public async Task<IActionResult> VerifyResetCode(string resetCode)
    {
        string username = TempData["ResetUsername"] as string;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(resetCode))
        {
            TempData["Error"] = "Invalid verification code or session expired";
            return RedirectToAction("ForgotPassword");
        }
        
        // Keep username for the next request
        TempData["ResetUsername"] = username;
        
        // Verify code
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            string query = @"
                SELECT pr.UserId 
                FROM PasswordResets pr
                INNER JOIN Users u ON pr.UserId = u.UserId
                WHERE u.Username = @Username
                AND pr.ResetCode = @ResetCode
                AND pr.ExpiresAt > GETDATE()
                AND pr.IsUsed = 0";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@ResetCode", resetCode);
                
                var userId = await command.ExecuteScalarAsync();
                
                if (userId != null)
                {
                    // Store verified code for password reset
                    TempData["VerifiedResetCode"] = resetCode;
                    
                    // Redirect to password reset page
                    return RedirectToAction("ResetPassword");
                }
                else
                {
                    ViewBag.Error = "Invalid or expired verification code";
                    return View();
                }
            }
        }
    }
    
    [HttpGet]
    public IActionResult ResetPassword()
    {
        string username = TempData["ResetUsername"] as string;
        string verifiedCode = TempData["VerifiedResetCode"] as string;
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifiedCode))
        {
            TempData["Error"] = "Please complete the verification process first";
            return RedirectToAction("ForgotPassword");
        }
        
        // Keep values for post request
        TempData["ResetUsername"] = username;
        TempData["VerifiedResetCode"] = verifiedCode;
        
        return View();
    }
    
    [HttpPost]
    public async Task<IActionResult> ResetPassword(string newPassword, string confirmPassword)
    {
        string username = TempData["ResetUsername"] as string;
        string verifiedCode = TempData["VerifiedResetCode"] as string;
        
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(verifiedCode))
        {
            TempData["Error"] = "Please complete the verification process first";
            return RedirectToAction("ForgotPassword");
        }
        
        if (string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
        {
            TempData["ResetUsername"] = username;
            TempData["VerifiedResetCode"] = verifiedCode;
            
            ViewBag.Error = "Both password fields are required";
            return View();
        }
        
        if (newPassword != confirmPassword)
        {
            TempData["ResetUsername"] = username;
            TempData["VerifiedResetCode"] = verifiedCode;
            
            ViewBag.Error = "Passwords do not match";
            return View();
        }
        
        // Validate password meets minimum requirements
        if (newPassword.Length < 8 || !newPassword.Any(char.IsUpper) || !newPassword.Any(c => !char.IsLetterOrDigit(c)))
        {
            TempData["ResetUsername"] = username;
            TempData["VerifiedResetCode"] = verifiedCode;
            
            ViewBag.Error = "Password must be at least 8 characters long, contain at least one uppercase letter, and one symbol";
            return View();
        }
        
        // Check if reset code is valid and update password
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            string query = @"
                SELECT pr.UserId 
                FROM PasswordResets pr
                INNER JOIN Users u ON pr.UserId = u.UserId
                WHERE u.Username = @Username
                AND pr.ResetCode = @ResetCode
                AND pr.ExpiresAt > GETDATE()
                AND pr.IsUsed = 0";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@ResetCode", verifiedCode);
                
                var userId = await command.ExecuteScalarAsync() as string;
                
                if (userId != null)
                {
                    // Hash the new password
                    string hashedPassword = PasswordHashService.HashPassword(newPassword);
                    
                    // Update password and reset NeedsPasswordChange flag
                    string updateQuery = "UPDATE Users SET Password = @Password, NeedsPasswordChange = 0 WHERE UserId = @UserId";
                    using (var updateCommand = new SqlCommand(updateQuery, connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Password", hashedPassword);
                        updateCommand.Parameters.AddWithValue("@UserId", userId);
                        await updateCommand.ExecuteNonQueryAsync();
                    }
                    
                    // Mark reset code as used
                    string markUsedQuery = "UPDATE PasswordResets SET IsUsed = 1 WHERE UserId = @UserId AND ResetCode = @ResetCode";
                    using (var markUsedCommand = new SqlCommand(markUsedQuery, connection))
                    {
                        markUsedCommand.Parameters.AddWithValue("@UserId", userId);
                        markUsedCommand.Parameters.AddWithValue("@ResetCode", verifiedCode);
                        await markUsedCommand.ExecuteNonQueryAsync();
                    }
                    
                    TempData["Success"] = "Password has been reset successfully";
                    return RedirectToAction("Login", "Home");
                }
                else
                {
                    TempData["Error"] = "Invalid or expired verification code";
                    return RedirectToAction("ForgotPassword");
                }
            }
        }
    }

    private string GenerateResetCode()
    {
        // Generate a 6-digit code
        Random random = new Random();
        return random.Next(100000, 999999).ToString();
    }
    
    private async Task StoreResetCode(string userId, string resetCode)
    {
        using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            
            // Check if PasswordResets table exists
            bool tableExists = false;
            string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PasswordResets'";
            using (var tableCommand = new SqlCommand(checkTableQuery, connection))
            {
                int tableCount = (int)await tableCommand.ExecuteScalarAsync();
                tableExists = tableCount > 0;
            }
            
            // Create table if it doesn't exist
            if (!tableExists)
            {
                string createTableQuery = @"
                    CREATE TABLE PasswordResets (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        UserId NVARCHAR(50) NOT NULL,
                        ResetCode NVARCHAR(10) NOT NULL,
                        ExpiresAt DATETIME NOT NULL,
                        CreatedAt DATETIME DEFAULT GETDATE(),
                        IsUsed BIT DEFAULT 0
                    )";
                
                using (var createCommand = new SqlCommand(createTableQuery, connection))
                {
                    await createCommand.ExecuteNonQueryAsync();
                }
            }
            
            // Delete any existing reset codes for this user
            string deleteQuery = "DELETE FROM PasswordResets WHERE UserId = @UserId";
            using (var deleteCommand = new SqlCommand(deleteQuery, connection))
            {
                deleteCommand.Parameters.AddWithValue("@UserId", userId);
                await deleteCommand.ExecuteNonQueryAsync();
            }
            
            // Insert new reset code
            string insertQuery = @"
                INSERT INTO PasswordResets (UserId, ResetCode, ExpiresAt)
                VALUES (@UserId, @ResetCode, DATEADD(HOUR, 1, GETDATE()))";
            
            using (var insertCommand = new SqlCommand(insertQuery, connection))
            {
                insertCommand.Parameters.AddWithValue("@UserId", userId);
                insertCommand.Parameters.AddWithValue("@ResetCode", resetCode);
                await insertCommand.ExecuteNonQueryAsync();
            }
        }
    }

    [HttpGet]
    public IActionResult CheckLockout()
    {
        string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string username = HttpContext.Request.Cookies["lastUsername"] ?? "";
        string lockoutKey = $"{username}_{ipAddress}";
        
        bool isLocked = false;
        int remainingSeconds = 0;
        
        // First check session storage for lockout info
        string sessionLockoutKey = "Lockout_EndTime";
        if (HttpContext.Session.TryGetValue(sessionLockoutKey, out byte[] lockoutTimeBytes))
        {
            string lockoutTimeStr = System.Text.Encoding.UTF8.GetString(lockoutTimeBytes);
            if (DateTime.TryParse(lockoutTimeStr, out DateTime lockoutEndTime))
            {
                if (DateTime.Now < lockoutEndTime)
                {
                    isLocked = true;
                    remainingSeconds = (int)(lockoutEndTime - DateTime.Now).TotalSeconds;
                }
                else
                {
                    // Lockout period has ended, remove from session
                    HttpContext.Session.Remove(sessionLockoutKey);
                }
            }
        }
        
        // If not locked by session, check static dictionary
        if (!isLocked && _lockoutTimes.ContainsKey(lockoutKey))
        {
            DateTime lockoutEndTime = _lockoutTimes[lockoutKey];
            if (DateTime.Now < lockoutEndTime)
            {
                isLocked = true;
                remainingSeconds = (int)(lockoutEndTime - DateTime.Now).TotalSeconds;
                
                // Also store in session for better persistence
                HttpContext.Session.SetString(sessionLockoutKey, lockoutEndTime.ToString("o"));
            }
            else
            {
                // Lockout period has ended, remove from dictionary
                _lockoutTimes.Remove(lockoutKey);
                _failedLoginAttempts.Remove(lockoutKey);
            }
        }
        
        return Json(new { isLocked, remainingSeconds });
    }
}
