using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudentBadge.Models;
using StudentBadge.Services;
using System.IO;
using System.Linq;

[Route("Dashboard")]
public class EmployerController : Controller
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmployerController> _logger;
    private readonly IWebHostEnvironment _hostingEnvironment;
    private readonly DatabaseUtilityService _dbUtilityService;
    private readonly MarkedStudentsService _markedStudentsService;

    public EmployerController(IConfiguration configuration, 
        ILogger<EmployerController> logger, 
        IWebHostEnvironment hostingEnvironment,
        DatabaseUtilityService dbUtilityService,
        MarkedStudentsService markedStudentsService)
    {
        _configuration = configuration;
        _logger = logger;
        _hostingEnvironment = hostingEnvironment;
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _dbUtilityService = dbUtilityService;
        _markedStudentsService = markedStudentsService;
    }

    // Helper method for checking if a column exists in a table
    private async Task<bool> CheckColumnExists(SqlConnection conn, string tableName, string columnName)
    {
        string query = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                
        using (var command = new SqlCommand(query, conn))
        {
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ColumnName", columnName);
            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }
    }

    [HttpGet("EmployerDashboard")]
    // Add this action method to handle the Employer Dashboard
    public async Task<IActionResult> EmployerDashboard()
    {
        // Get employer info from session
        string employerId = HttpContext.Session.GetString("EmployerId");
        ViewBag.EmployerName = HttpContext.Session.GetString("FullName");
        ViewBag.CompanyName = HttpContext.Session.GetString("Company");
        
        // Make sure we have a valid employer ID
        if (string.IsNullOrEmpty(employerId))
        {
            _logger.LogWarning("EmployerDashboard: No employer ID found in session!");
            // Redirect to login if no valid employer ID
            return RedirectToAction("Login", "Home");
        }
        
        // Double-check that the employer exists in database with this ID
        bool employerExists = false;
        try
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check Users table first (if it exists)
                bool usersTableExists = false;
                string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users'";
                using (var checkCmd = new SqlCommand(checkTableQuery, conn))
                {
                    usersTableExists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                }
                
                if (usersTableExists)
                {
                    string checkUserQuery = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId AND Role = 'employer'";
                    using (var cmd = new SqlCommand(checkUserQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", employerId);
                        int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                        employerExists = count > 0;
                    }
                }
                
                // If not found and old Employers table exists, check there
                if (!employerExists)
                {
                    bool employersTableExists = false;
                    checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Employers'";
                    using (var checkCmd = new SqlCommand(checkTableQuery, conn))
                    {
                        employersTableExists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                    }
                    
                    if (employersTableExists)
                    {
                        string checkEmployerQuery = "SELECT COUNT(*) FROM Employers WHERE EmployerId = @EmployerId";
                        using (var cmd = new SqlCommand(checkEmployerQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@EmployerId", employerId);
                            int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            employerExists = count > 0;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating employer: {ex.Message}");
            // Continue anyway since we at least have the ID in session
            employerExists = true;
        }
        
        if (!employerExists)
        {
            _logger.LogWarning($"EmployerDashboard: Employer ID {employerId} not found in database");
            // We'll continue anyway but log the warning
        }
        
        // Log the employer ID
        _logger.LogInformation($"EmployerDashboard: Loading for employer ID: {employerId}");
        
        // Set the employer ID in ViewBag
        ViewBag.EmployerId = employerId;
        
        // Store the employer ID in TempData as well for additional redundancy
        TempData["EmployerId"] = employerId;
        
        // Get all students to display in the employer dashboard
        var allStudents = await GetAllStudentsWithDetails();
        ViewBag.AllStudents = allStudents;

        return View("~/Views/Dashboard/EmployerDashboard.cshtml");
    }

    [HttpGet("EmployerProfile")]
    // Add this action method to handle the Employer Profile page
    public async Task<IActionResult> EmployerProfile()
    {
        try
        {
            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                // If no employer ID in session, redirect to login
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction("Login", "Home");
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    // Check which columns exist
                    bool hasEmailInUsers = await _dbUtilityService.ColumnExists(conn, "Users", "Email");
                    bool hasPhoneInUsers = await _dbUtilityService.ColumnExists(conn, "Users", "PhoneNumber");
                    bool hasProfilePicInEmployerDetails = await _dbUtilityService.ColumnExists(conn, "EmployerDetails", "ProfilePicturePath");

                    // Build query for new table structure (Users + EmployerDetails)
                    query = @"
                        SELECT 
                            u.FullName,
                            u.Username,
                            ed.Company,
                            ed.Description";
                    
                    if (hasEmailInUsers)
                    {
                        query += ", u.Email";
                    }
                    
                    if (hasPhoneInUsers)
                    {
                        query += ", u.PhoneNumber";
                    }
                    
                    if (hasProfilePicInEmployerDetails)
                    {
                        query += ", ed.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM Users u
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE u.UserId = @EmployerId";
                }
                else
                {
                    // Check if old Employers table has these columns
                    bool hasEmailColumn = await _dbUtilityService.ColumnExists(conn, "Employers", "Email");
                    bool hasPhoneColumn = await _dbUtilityService.ColumnExists(conn, "Employers", "PhoneNumber");
                    bool hasProfilePicColumn = await _dbUtilityService.ColumnExists(conn, "Employers", "ProfilePicturePath");

                    // Build query for old table structure (Employers)
                    query = @"
                        SELECT 
                            FullName,
                            Username,
                            Company,
                            Description";
                    
                    if (hasEmailColumn)
                    {
                        query += ", Email";
                    }
                    
                    if (hasPhoneColumn)
                    {
                        query += ", PhoneNumber";
                    }
                    
                    if (hasProfilePicColumn)
                    {
                        query += ", ProfilePicturePath";
                    }
                    
                    query += " FROM Employers WHERE EmployerId = @EmployerId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Set ViewBag values for the view
                            ViewBag.EmployerName = reader["FullName"].ToString();
                            ViewBag.Username = reader["Username"].ToString();
                            ViewBag.CompanyName = reader["Company"].ToString();
                            ViewBag.Description = reader["Description"]?.ToString() ?? "";
                            
                            // Check if columns exist in the result set
                            bool hasEmail = reader.HasColumn("Email");
                            bool hasPhoneNumber = reader.HasColumn("PhoneNumber");
                            bool hasProfilePicture = reader.HasColumn("ProfilePicturePath");

                            ViewBag.Email = hasEmail ? reader["Email"]?.ToString() ?? "" : "";
                            ViewBag.PhoneNumber = hasPhoneNumber ? reader["PhoneNumber"]?.ToString() ?? "" : "";
                            ViewBag.ProfilePicturePath = hasProfilePicture && reader["ProfilePicturePath"] != DBNull.Value
                                ? reader["ProfilePicturePath"].ToString()
                                : "/images/blank.jpg";

                            return View("~/Views/Dashboard/EmployerProfile.cshtml");
                        }
                        else
                        {
                            // Employer not found in database, but we have session
                            // Just display with session values as backup
                            ViewBag.EmployerName = HttpContext.Session.GetString("FullName") ?? "Unknown";
                            ViewBag.Username = "";
                            ViewBag.CompanyName = HttpContext.Session.GetString("Company") ?? "";
                            ViewBag.Description = "";
                            ViewBag.Email = "";
                            ViewBag.PhoneNumber = "";
                            ViewBag.ProfilePicturePath = "/images/blank.jpg";
                            
                            return View("~/Views/Dashboard/EmployerProfile.cshtml");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in EmployerProfile: {ex.Message}");
            
            // Fall back to session values in case of error
            ViewBag.EmployerName = HttpContext.Session.GetString("FullName") ?? "Unknown";
            ViewBag.Username = "";
            ViewBag.CompanyName = HttpContext.Session.GetString("Company") ?? "";
            ViewBag.Description = "";
            ViewBag.Email = "";
            ViewBag.PhoneNumber = "";
            ViewBag.ProfilePicturePath = "/images/blank.jpg";
            
            return View("~/Views/Dashboard/EmployerProfile.cshtml");
        }
    }

    [HttpGet("GetStudentProfileForEmployer")]
    public async Task<IActionResult> GetStudentProfileForEmployer(string studentId)
    {
        if (string.IsNullOrEmpty(studentId))
        {
            return Json(new { success = false, message = "Student ID is required" });
        }

        try
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                // Check if grade columns exist
                bool gradeColumnsExist = false;
                if (useNewTable)
                {
                    var checkColumnsCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'FirstYearGrade'",
                        conn
                    );
                    gradeColumnsExist = (int)await checkColumnsCmd.ExecuteScalarAsync() > 0;
                }

                string query;
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            u.FullName,
                            sd.IdNumber,
                            sd.Course,
                            sd.Section,
                            sd.Score,
                            sd.Achievements,
                            sd.Comments,
                            sd.BadgeColor,
                            sd.ProfilePicturePath,
                            sd.IsProfileVisible,
                            sd.IsResumeVisible,
                            sd.ResumeFileName";
                            
                    // Only add grade columns if they exist
                    if (gradeColumnsExist)
                    {
                        query += @",
                            sd.FirstYearGrade,
                            sd.SecondYearGrade,
                            sd.ThirdYearGrade,
                            sd.FourthYearGrade";
                    }
                            
                    query += @"
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId AND sd.IsProfileVisible = 1";
                }
                else
                {
                    query = @"
                        SELECT 
                            FullName,
                            IdNumber,
                            Course,
                            Section,
                            Score,
                            Achievements,
                            Comments,
                            BadgeColor,
                            ProfilePicturePath,
                            1 as IsProfileVisible,
                            IsResumeVisible,
                            ResumeFileName
                        FROM Students
                        WHERE IdNumber = @StudentId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Create student object
                            var studentDict = new Dictionary<string, object>
                            {
                                { "FullName", reader["FullName"].ToString() },
                                { "IdNumber", reader["IdNumber"].ToString() },
                                { "Course", reader["Course"].ToString() },
                                { "Section", reader["Section"].ToString() },
                                { "Score", reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0 },
                                { "Achievements", reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "" },
                                { "Comments", reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "" },
                                { "BadgeColor", reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None" },
                                { "ProfilePicture", reader["ProfilePicturePath"] != DBNull.Value ? reader["ProfilePicturePath"].ToString() : "/images/blank.jpg" },
                                { "IsProfileVisible", Convert.ToBoolean(reader["IsProfileVisible"]) },
                                { "IsResumeVisible", Convert.ToBoolean(reader["IsResumeVisible"]) },
                                { "Resume", reader["ResumeFileName"] != DBNull.Value ? reader["ResumeFileName"].ToString() : null }
                            };
                            
                            // Add grade columns if they exist
                            if (gradeColumnsExist)
                            {
                                studentDict["FirstYearGrade"] = reader["FirstYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FirstYearGrade"]) : null;
                                studentDict["SecondYearGrade"] = reader["SecondYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["SecondYearGrade"]) : null;
                                studentDict["ThirdYearGrade"] = reader["ThirdYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["ThirdYearGrade"]) : null;
                                studentDict["FourthYearGrade"] = reader["FourthYearGrade"] != DBNull.Value ? Convert.ToDecimal(reader["FourthYearGrade"]) : null;
                            }
                            else
                            {
                                // Add null values for grades if columns don't exist
                                studentDict["FirstYearGrade"] = null;
                                studentDict["SecondYearGrade"] = null;
                                studentDict["ThirdYearGrade"] = null;
                                studentDict["FourthYearGrade"] = null;
                            }

                            return Json(new { success = true, student = studentDict });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student not found or profile not visible" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student profile for employer");
            return Json(new { success = false, message = "Error loading profile" });
        }
    }

    [HttpGet("GetStudentProfile")]
    public IActionResult GetStudentProfile(string studentId)
    {
        try
        {
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student ID is required." });
            }

            using (SqlConnection conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'",
                    conn
                );
                bool useNewTable = (int)checkTableCmd.ExecuteScalar() > 0;

                string query;
                if (useNewTable)
                {
                    query = @"
                        SELECT 
                            u.FullName,
                            sd.IdNumber,
                            sd.Course,
                            sd.Section,
                            sd.Score,
                            sd.Achievements,
                            sd.Comments,
                            sd.BadgeColor,
                            sd.ProfilePicturePath,
                            sd.IsProfileVisible,
                            sd.IsResumeVisible,
                            sd.ResumeFileName
                        FROM StudentDetails sd
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE sd.IdNumber = @StudentId AND sd.IsProfileVisible = 1";
                }
                else
                {
                    query = @"
                        SELECT 
                            FullName,
                            IdNumber,
                            Course,
                            Section,
                            Score,
                            Achievements,
                            Comments,
                            BadgeColor,
                            ProfilePicturePath,
                            1 as IsProfileVisible,
                            IsResumeVisible,
                            ResumeFileName
                        FROM Students
                        WHERE IdNumber = @StudentId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@StudentId", studentId);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Get the student details from the reader
                            var studentInfo = new
                            {
                                fullName = reader["FullName"].ToString(),
                                idNumber = reader["IdNumber"].ToString(),
                                course = reader["Course"].ToString(),
                                section = reader["Section"].ToString(),
                                score = Convert.ToInt32(reader["Score"]),
                                achievements = reader["Achievements"].ToString(),
                                comments = reader["Comments"].ToString(),
                                badgeColor = reader["BadgeColor"].ToString(),
                                profilePicture = Url.Action("GetProfilePicture", "Dashboard", new { studentId = reader["IdNumber"].ToString(), t = DateTime.Now.Ticks }),
                                isProfileVisible = Convert.ToBoolean(reader["IsProfileVisible"]),
                                isResumeVisible = Convert.ToBoolean(reader["IsResumeVisible"]),
                                resume = reader["ResumeFileName"] != DBNull.Value ? reader["ResumeFileName"].ToString() : null
                            };

                            // Now get completed challenges for the student
                            List<object> completedChallenges = new List<object>();
                            
                            string userId = null;

                            // First, get the UserId for this student
                            string getUserIdQuery = @"
                                SELECT UserId 
                                FROM Users 
                                WHERE Username = @IdNumber";
                            
                            using (SqlCommand userCmd = new SqlCommand(getUserIdQuery, conn))
                            {
                                userCmd.Parameters.AddWithValue("@IdNumber", studentId);
                                var userIdResult = userCmd.ExecuteScalar();
                                
                                if (userIdResult != null && userIdResult != DBNull.Value)
                                {
                                    userId = userIdResult.ToString();
                                }
                            }

                            if (!string.IsNullOrEmpty(userId))
                            {
                                string challengeQuery = @"
                                    SELECT s.*, c.ChallengeName as ChallengeName, c.ProgrammingLanguage, c.Description
                                    FROM ChallengeSubmissions s
                                    JOIN Challenges c ON s.ChallengeId = c.ChallengeId
                                    WHERE s.StudentId = @StudentId
                                    ORDER BY s.SubmissionDate DESC";
                                
                                using (SqlCommand challengeCmd = new SqlCommand(challengeQuery, conn))
                                {
                                    challengeCmd.Parameters.AddWithValue("@StudentId", userId);
                                    
                                    using (SqlDataReader challengeReader = challengeCmd.ExecuteReader())
                                    {
                                        while (challengeReader.Read())
                                        {
                                            completedChallenges.Add(new
                                            {
                                                challengeName = challengeReader["ChallengeName"].ToString(),
                                                programmingLanguage = challengeReader["ProgrammingLanguage"].ToString(),
                                                description = challengeReader["Description"] != DBNull.Value ? challengeReader["Description"].ToString() : "",
                                                submissionDate = Convert.ToDateTime(challengeReader["SubmissionDate"]).ToString("yyyy-MM-dd"),
                                                percentageScore = challengeReader["PercentageScore"] != DBNull.Value ? Convert.ToInt32(challengeReader["PercentageScore"]) : 0
                                            });
                                        }
                                    }
                                }
                            }

                            // Return student details and completed challenges in the response
                            var profileData = new
                            {
                                success = true,
                                fullName = studentInfo.fullName,
                                idNumber = studentInfo.idNumber,
                                course = studentInfo.course,
                                section = studentInfo.section,
                                score = studentInfo.score,
                                achievements = studentInfo.achievements,
                                comments = studentInfo.comments,
                                badgeColor = studentInfo.badgeColor,
                                profilePicture = studentInfo.profilePicture,
                                isProfileVisible = studentInfo.isProfileVisible,
                                isResumeVisible = studentInfo.isResumeVisible,
                                resume = studentInfo.resume,
                                completedChallenges = completedChallenges
                            };

                            return Json(profileData);
                        }
                        else
                        {
                            return Json(new { success = false, message = "Student profile not found or not visible." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetStudentProfile: {ex.Message}");
            return Json(new { success = false, message = "Error loading profile. Please try again." });
        }
    }

  [HttpGet]
  [Route("GetEmployerDetails")]
    public async Task<IActionResult> GetEmployerDetails(string employerId)
    {
        try
        {
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID is required." });
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if new table structure exists
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool useNewTable = (int)await checkTableCmd.ExecuteScalarAsync() > 0;

                string query;
                if (useNewTable)
                {
                    // Check which columns exist
                    bool hasEmailInUsers = await CheckColumnExists(conn, "Users", "Email");
                    bool hasPhoneInUsers = await CheckColumnExists(conn, "Users", "PhoneNumber");
                    bool hasProfilePicInEmployerDetails = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");

                    // Build query for new table structure (Users + EmployerDetails)
                    query = @"
                        SELECT 
                            u.FullName,
                            u.Username,
                            ed.Company,
                            ed.Description";
                    
                    if (hasEmailInUsers)
                    {
                        query += ", u.Email";
                    }
                    
                    if (hasPhoneInUsers)
                    {
                        query += ", u.PhoneNumber";
                    }
                    
                    if (hasProfilePicInEmployerDetails)
                    {
                        query += ", ed.ProfilePicturePath";
                    }
                    
                    query += @"
                        FROM Users u
                        JOIN EmployerDetails ed ON u.UserId = ed.UserId
                        WHERE u.UserId = @EmployerId";
                }
                else
                {
                    // Check if old Employers table has these columns
                    bool hasEmailColumn = await CheckColumnExists(conn, "Employers", "Email");
                    bool hasPhoneColumn = await CheckColumnExists(conn, "Employers", "PhoneNumber");
                    bool hasProfilePicColumn = await CheckColumnExists(conn, "Employers", "ProfilePicturePath");

                    // Build query for old table structure
                    query = @"
                        SELECT 
                            FullName,
                            Username,
                            Company,
                            Description";
                    
                    if (hasEmailColumn)
                    {
                        query += ", Email";
                    }
                    
                    if (hasPhoneColumn)
                    {
                        query += ", PhoneNumber";
                    }
                    
                    if (hasProfilePicColumn)
                    {
                        query += ", ProfilePicturePath";
                    }
                    
                    query += " FROM Employers WHERE EmployerId = @EmployerId";
                }

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Check if columns exist in the result set
                            bool hasEmail = reader.HasColumn("Email");
                            bool hasPhoneNumber = reader.HasColumn("PhoneNumber");
                            bool hasProfilePicture = reader.HasColumn("ProfilePicturePath");
                            
                            var employer = new
                            {
                                fullName = reader["FullName"].ToString(),
                                username = reader["Username"].ToString(),
                                company = reader["Company"].ToString(),
                                description = reader["Description"]?.ToString() ?? "",
                                email = hasEmail ? reader["Email"]?.ToString() ?? "" : "",
                                phoneNumber = hasPhoneNumber ? reader["PhoneNumber"]?.ToString() ?? "" : "",
                                profilePicturePath = hasProfilePicture && reader["ProfilePicturePath"] != DBNull.Value
                                    ? reader["ProfilePicturePath"].ToString()
                                    : null
                            };

                            return Json(new { success = true, employer = employer });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Employer not found." });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetEmployerDetails: {ex.Message}");
            return Json(new { success = false, message = "Error loading employer details. Please try again." });
        }
    }

 
    [HttpPost("UpdateEmployerProfileForm")]
    public async Task<IActionResult> UpdateEmployerProfileForm(IFormCollection form)
    {
        try
        {
            // Get employer ID from session
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                // If no employer ID in session, redirect to login
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction("Login", "Home");
            }

            // Extract form data
            string fullName = form["FullName"].ToString();
            string username = form["Username"].ToString();
            string companyName = form["CompanyName"].ToString();
            string email = form["Email"].ToString();
            string phoneNumber = form["PhoneNumber"].ToString();
            string description = form["Description"].ToString();
            string password = form["Password"].ToString();

            // Allow update even if some fields are empty - removed strict validation

            // Handle profile picture upload
            string profilePicturePath = null;
            
            // Check if we have a base64 image
            if (form.ContainsKey("ProfilePictureBase64"))
            {
                string base64Image = form["ProfilePictureBase64"];
                
                if (!string.IsNullOrEmpty(base64Image) && base64Image.Contains(","))
                {
                    // Extract the data part from the base64 string
                    string[] parts = base64Image.Split(',');
                    string base64Data = parts[1];
                    
                    try
                    {
                        // Convert base64 to bytes
                        byte[] imageBytes = Convert.FromBase64String(base64Data);
                        
                        // Generate a unique filename
                        string fileName = $"employer_{employerId}_{DateTime.Now.Ticks}.jpg";
                        string filePath = Path.Combine(_hostingEnvironment.WebRootPath, "images", "profiles", fileName);
                        
                        // Ensure directory exists
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        
                        // Save the file
                        System.IO.File.WriteAllBytes(filePath, imageBytes);
                        
                        // Set profile picture path
                        profilePicturePath = $"/images/profiles/{fileName}";
                        _logger.LogInformation($"Saved profile picture from base64 to {profilePicturePath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing base64 image: {ex.Message}");
                    }
                }
            }
            // Handle regular file upload if no base64 image
            else if (form.Files.Count > 0 && form.Files["ProfilePicture"] != null && form.Files["ProfilePicture"].Length > 0)
            {
                try
                {
                    var file = form.Files["ProfilePicture"];
                    _logger.LogInformation($"Processing file upload: {file.FileName}, size: {file.Length} bytes");
                    
                    // Generate a unique filename
                    string fileName = $"employer_{employerId}_{DateTime.Now.Ticks}{Path.GetExtension(file.FileName)}";
                    string filePath = Path.Combine(_hostingEnvironment.WebRootPath, "images", "profiles", fileName);
                    
                    // Ensure directory exists
                    string directory = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directory))
                    {
                        _logger.LogInformation($"Creating directory: {directory}");
                        Directory.CreateDirectory(directory);
                    }
                    
                    // Save the file
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    
                    // Set profile picture path
                    profilePicturePath = $"/images/profiles/{fileName}";
                    _logger.LogInformation($"Saved profile picture to {profilePicturePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error uploading profile picture: {ex.Message}");
                }
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                
                // Check if we're using the new table structure
                var checkTableCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EmployerDetails'",
                    conn
                );
                bool newTableExists = (int)await checkTableCmd.ExecuteScalarAsync() > 0;
                _logger.LogInformation($"Using new table structure: {newTableExists}");
                
                if (newTableExists)
                {
                    // Check if EmployerDetails has a ProfilePicturePath column
                    var hasProfilePicColumn = await CheckColumnExists(conn, "EmployerDetails", "ProfilePicturePath");
                    _logger.LogInformation($"EmployerDetails has ProfilePicturePath column: {hasProfilePicColumn}");
                    
                    // If EmployerDetails doesn't have the column, add it
                    if (!hasProfilePicColumn && profilePicturePath != null)
                    {
                        try
                        {
                            _logger.LogInformation("Adding ProfilePicturePath column to EmployerDetails table");
                            string addColumnQuery = "ALTER TABLE EmployerDetails ADD ProfilePicturePath NVARCHAR(255) NULL";
                            using (var addColumnCmd = new SqlCommand(addColumnQuery, conn))
                            {
                                await addColumnCmd.ExecuteNonQueryAsync();
                            }
                            hasProfilePicColumn = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error adding ProfilePicturePath column: {ex.Message}");
                        }
                    }
                    
                    // Update the Users table first (contains username, email, phone, password)
                    string usersUpdateQuery = "UPDATE Users SET FullName = @FullName, Username = @Username";
                    
                    // Check if Users table has Email and PhoneNumber columns
                    bool hasEmailColumn = await CheckColumnExists(conn, "Users", "Email");
                    bool hasPhoneColumn = await CheckColumnExists(conn, "Users", "PhoneNumber");
                    
                    if (hasEmailColumn)
                    {
                        usersUpdateQuery += ", Email = @Email";
                    }
                    
                    if (hasPhoneColumn)
                    {
                        usersUpdateQuery += ", PhoneNumber = @PhoneNumber"; 
                    }
                    
                    if (!string.IsNullOrEmpty(password))
                    {
                        usersUpdateQuery += ", Password = @Password";
                    }
                    
                    usersUpdateQuery += " WHERE UserId = @EmployerId";
                    
                    using (SqlCommand usersCmd = new SqlCommand(usersUpdateQuery, conn))
                    {
                        usersCmd.Parameters.AddWithValue("@FullName", fullName);
                        usersCmd.Parameters.AddWithValue("@Username", username);
                        usersCmd.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        if (hasEmailColumn)
                        {
                            usersCmd.Parameters.AddWithValue("@Email", email);
                        }
                        
                        if (hasPhoneColumn)
                        {
                            usersCmd.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
                        }
                        
                        if (!string.IsNullOrEmpty(password))
                        {
                            usersCmd.Parameters.AddWithValue("@Password", password);
                        }
                        
                        await usersCmd.ExecuteNonQueryAsync();
                    }
                    
                    // Now update the EmployerDetails table
                    string detailsUpdateQuery = "UPDATE EmployerDetails SET Company = @Company, Description = @Description";
                    
                    if (hasProfilePicColumn && profilePicturePath != null)
                    {
                        detailsUpdateQuery += ", ProfilePicturePath = @ProfilePicturePath";
                    }
                    
                    detailsUpdateQuery += " WHERE UserId = @EmployerId";
                    
                    using (SqlCommand detailsCmd = new SqlCommand(detailsUpdateQuery, conn))
                    {
                        detailsCmd.Parameters.AddWithValue("@Company", companyName);
                        detailsCmd.Parameters.AddWithValue("@Description", description);
                        detailsCmd.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        if (hasProfilePicColumn && profilePicturePath != null)
                        {
                            detailsCmd.Parameters.AddWithValue("@ProfilePicturePath", profilePicturePath);
                            _logger.LogInformation($"Updating ProfilePicturePath to {profilePicturePath} in EmployerDetails");
                        }
                        
                        await detailsCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Check if Employers has a ProfilePicturePath column
                    var hasProfilePicColumn = await CheckColumnExists(conn, "Employers", "ProfilePicturePath");
                    _logger.LogInformation($"Employers table has ProfilePicturePath column: {hasProfilePicColumn}");
                    
                    // If Employers doesn't have the column, add it
                    if (!hasProfilePicColumn && profilePicturePath != null)
                    {
                        try
                        {
                            _logger.LogInformation("Adding ProfilePicturePath column to Employers table");
                            string addColumnQuery = "ALTER TABLE Employers ADD ProfilePicturePath NVARCHAR(255) NULL";
                            using (var addColumnCmd = new SqlCommand(addColumnQuery, conn))
                            {
                                await addColumnCmd.ExecuteNonQueryAsync();
                            }
                            hasProfilePicColumn = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error adding ProfilePicturePath column: {ex.Message}");
                        }
                    }
                    
                    // Update query for the Employers table
                    string updateQuery = @"
                        UPDATE Employers 
                        SET FullName = @FullName, 
                            Username = @Username,
                            Company = @Company,
                            Description = @Description";
                    
                    if (!string.IsNullOrEmpty(password))
                    {
                        updateQuery += ", Password = @Password";
                    }
                    
                    if (hasProfilePicColumn && profilePicturePath != null)
                    {
                        updateQuery += ", ProfilePicturePath = @ProfilePicturePath";
                    }
                    
                    updateQuery += " WHERE EmployerId = @EmployerId";
                    
                    using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@FullName", fullName);
                        cmd.Parameters.AddWithValue("@Username", username);
                        cmd.Parameters.AddWithValue("@Company", companyName);
                        cmd.Parameters.AddWithValue("@Description", description);
                        cmd.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        if (!string.IsNullOrEmpty(password))
                        {
                            cmd.Parameters.AddWithValue("@Password", password);
                        }
                        
                        if (hasProfilePicColumn && profilePicturePath != null)
                        {
                            cmd.Parameters.AddWithValue("@ProfilePicturePath", profilePicturePath);
                            _logger.LogInformation($"Updating ProfilePicturePath to {profilePicturePath} in Employers");
                        }
                        
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                
                // Update session values
                HttpContext.Session.SetString("FullName", fullName);
                HttpContext.Session.SetString("Company", companyName);
                if (profilePicturePath != null)
                {
                    HttpContext.Session.SetString("ProfilePicturePath", profilePicturePath);
                }
                
                TempData["Success"] = "Profile updated successfully!";
                return RedirectToAction("EmployerProfile");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in UpdateEmployerProfileForm: {ex.Message}");
            TempData["Error"] = "Error updating profile: " + ex.Message;
            return RedirectToAction("EmployerProfile");
        }
    }
    
    private async Task<List<Student>> GetAllStudentsWithDetails()
    {
        var allStudents = new List<Student>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            
            // Check if the StudentDetails table exists
            bool tableExists = false;
            string checkTableQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = 'StudentDetails'";
            
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                tableExists = (count > 0);
            }
            
            if (!tableExists)
            {
                // If the new table doesn't exist, return an empty list
                return allStudents;
            }
            
            // Check if GradeLevel column exists
            bool hasGradeLevelColumn = false;
            string checkColumnQuery = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'GradeLevel'";
                
            using (var command = new SqlCommand(checkColumnQuery, connection))
            {
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                hasGradeLevelColumn = (count > 0);
            }
            
            // Build the query based on whether GradeLevel exists
            string gradeLevelSelect = hasGradeLevelColumn ? ", sd.GradeLevel" : "";
            string query = $@"
                SELECT sd.IdNumber, u.FullName, sd.Course, sd.Section, 
                       sd.IsProfileVisible, sd.ProfilePicturePath, sd.ResumeFileName, 
                       sd.Score, sd.Achievements, sd.Comments, sd.BadgeColor, sd.IsResumeVisible{gradeLevelSelect}
                FROM StudentDetails sd
                JOIN Users u ON sd.UserId = u.UserId
                WHERE u.Role = 'student'
                ORDER BY sd.Score DESC";

            using (var command = new SqlCommand(query, connection))
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var student = new Student
                    {
                        IdNumber = reader["IdNumber"]?.ToString() ?? string.Empty,
                        FullName = reader["FullName"]?.ToString() ?? string.Empty,
                        Course = reader["Course"]?.ToString() ?? string.Empty,
                        Section = reader["Section"]?.ToString() ?? string.Empty,
                        IsProfileVisible = reader["IsProfileVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsProfileVisible"]),
                        ProfilePicturePath = reader["ProfilePicturePath"] as string,
                        ResumePath = reader["ResumeFileName"] as string,
                        Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                        Achievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : "",
                        Comments = reader["Comments"] != DBNull.Value ? reader["Comments"].ToString() : "",
                        BadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None",
                        IsResumeVisible = reader["IsResumeVisible"] != DBNull.Value && Convert.ToBoolean(reader["IsResumeVisible"])
                    };
                    
                    // Add grade level if it exists in the result
                    if (hasGradeLevelColumn && reader["GradeLevel"] != DBNull.Value)
                    {
                        student.GradeLevel = Convert.ToInt32(reader["GradeLevel"]);
                    }
                    else 
                    {
                        // Default to 0 (Unknown) if no grade level available
                        student.GradeLevel = 0;
                    }
                    
                    allStudents.Add(student);
                }
            }
        }

        // Filter out students with disabled profiles
        return allStudents.Where(s => s.IsProfileVisible).ToList();
    }

    [HttpGet("GetEmployerMessages")]
    public async Task<IActionResult> GetEmployerMessages(string employerId)
    {
        if (string.IsNullOrEmpty(employerId))
        {
            employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer ID not provided." });
            }
        }

        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if we're using the new tables
                bool usingNewTables = false;
                string checkTableQuery = @"
                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = 'EmployerStudentMessages'";
                
                using (var command = new SqlCommand(checkTableQuery, connection))
                {
                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                    usingNewTables = (count > 0);
                }
                
                string query;
                
                if (usingNewTables)
                {
                    query = @"
                        SELECT DISTINCT 
                            m.StudentId,
                            u.FullName as StudentName,
                            m.Message,
                            m.SentTime,
                            m.IsRead,
                            m.IsFromEmployer
                        FROM EmployerStudentMessages m
                        JOIN StudentDetails sd ON m.StudentId = sd.IdNumber
                        JOIN Users u ON sd.UserId = u.UserId
                        WHERE m.EmployerId = @EmployerId
                        AND m.SentTime IN (
                            SELECT MAX(SentTime)
                            FROM EmployerStudentMessages
                            WHERE EmployerId = @EmployerId
                            GROUP BY StudentId
                        )
                        ORDER BY m.SentTime DESC";
                }
                else
                {
                    query = @"
                        SELECT DISTINCT 
                            m.StudentId,
                            s.FullName as StudentName,
                            m.MessageContent as Message,
                            m.SentDateTime as SentTime,
                            m.IsRead,
                            m.IsFromEmployer
                        FROM Messages m
                        JOIN Students s ON m.StudentId = s.IdNumber
                        WHERE m.EmployerId = @EmployerId
                        AND m.SentDateTime IN (
                            SELECT MAX(SentDateTime)
                            FROM Messages
                            WHERE EmployerId = @EmployerId
                            GROUP BY StudentId
                        )
                        ORDER BY m.SentDateTime DESC";
                }

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    var messages = new List<object>();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            messages.Add(new
                            {
                                studentId = reader["StudentId"].ToString(),
                                studentName = reader["StudentName"].ToString(),
                                message = reader["Message"].ToString(),
                                sentTime = Convert.ToDateTime(reader["SentTime"]),
                                isRead = Convert.ToBoolean(reader["IsRead"]),
                                isFromEmployer = Convert.ToBoolean(reader["IsFromEmployer"])
                            });
                        }
                    }

                    return Json(new { success = true, messages = messages });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in GetEmployerMessages: {ex.Message}");
            return Json(new { success = false, message = "Error retrieving messages: " + ex.Message });
        }
    }

    [HttpGet("MarkedStudents")]
    public async Task<IActionResult> MarkedStudents()
    {
        // Get employer ID from session
        string employerId = HttpContext.Session.GetString("EmployerId");
        if (string.IsNullOrEmpty(employerId))
        {
            // If no employer ID in session, redirect to login
            TempData["Error"] = "Session expired. Please log in again.";
            return RedirectToAction("Login", "Home");
        }

        // Get all marked students
        var markedStudents = await _markedStudentsService.GetMarkedStudents(employerId);
        ViewBag.MarkedStudents = markedStudents;
        
        // Set employer info in ViewBag
        ViewBag.EmployerName = HttpContext.Session.GetString("FullName");
        ViewBag.CompanyName = HttpContext.Session.GetString("Company");
        ViewBag.EmployerId = employerId;

        return View("~/Views/Dashboard/MarkedStudents.cshtml");
    }

    [HttpPost("MarkStudent")]
    public async Task<IActionResult> MarkStudent(string studentId, string notes)
    {
        // Get employer ID from session
        string employerId = HttpContext.Session.GetString("EmployerId");
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Session expired. Please log in again." });
        }

        try
        {
            bool success = await _markedStudentsService.MarkStudent(employerId, studentId, notes);
            return Json(new { success = success, message = success ? "Student marked successfully" : "Failed to mark student" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking student {StudentId} by employer {EmployerId}", studentId, employerId);
            return Json(new { success = false, message = "An error occurred while marking the student" });
        }
    }

    [HttpPost("UnmarkStudent")]
    public async Task<IActionResult> UnmarkStudent(string studentId)
    {
        // Get employer ID from session
        string employerId = HttpContext.Session.GetString("EmployerId");
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Session expired. Please log in again." });
        }

        try
        {
            bool success = await _markedStudentsService.UnmarkStudent(employerId, studentId);
            return Json(new { success = success, message = success ? "Student unmarked successfully" : "Failed to unmark student" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmarking student {StudentId} by employer {EmployerId}", studentId, employerId);
            return Json(new { success = false, message = "An error occurred while unmarking the student" });
        }
    }

    [HttpGet("GetMarkedStudents")]
    public async Task<IActionResult> GetMarkedStudents()
    {
        // Get employer ID from session
        string employerId = HttpContext.Session.GetString("EmployerId");
        if (string.IsNullOrEmpty(employerId))
        {
            return Json(new { success = false, message = "Session expired. Please log in again." });
        }

        try
        {
            var markedStudents = await _markedStudentsService.GetMarkedStudents(employerId);
            
            // Convert to a custom format that matches JavaScript expectations
            var result = markedStudents.Select(s => new {
                id = s.Id,
                employerId = s.EmployerId,
                studentId = s.StudentId,  // lowercase to match JavaScript
                dateMarked = s.DateMarked,
                notes = s.Notes,
                studentName = s.StudentName,
                course = s.Course,
                section = s.Section,
                score = s.Score,
                badgeColor = s.BadgeColor,
                profilePicturePath = s.ProfilePicturePath
            });
            
            return Json(new { success = true, markedStudents = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting marked students for employer {EmployerId}", employerId);
            return Json(new { success = false, message = "An error occurred while fetching marked students" });
        }
    }
}
    