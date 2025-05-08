using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using StudentBadge.Models;
using StudentBadge.Data;
using Microsoft.Extensions.Configuration;
using StudentBadge.Utils;
using StudentBadge.Services;

namespace StudentBadge.Controllers
{
    public class AdminController : Controller
    {
        private readonly StudentContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;

        public AdminController(StudentContext context, IWebHostEnvironment webHostEnvironment, IConfiguration configuration)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
            // Configure EPPlus to use noncommercial license
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public IActionResult Index()
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Get student count without relying on _context.Students
            int studentCount = 0;
            
            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    
                    // Check if StudentDetails table exists
                    bool studentDetailsExists = false;
                    string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'";
                    using (var cmd = new SqlCommand(checkTableQuery, connection))
                    {
                        int result = Convert.ToInt32(cmd.ExecuteScalar());
                        studentDetailsExists = result > 0;
                    }
                    
                    if (studentDetailsExists)
                    {
                        // Count students using StudentDetails + Users
                        string countQuery = @"
                            SELECT COUNT(*) FROM StudentDetails sd
                            JOIN Users u ON sd.UserId = u.UserId
                            WHERE u.Role = 'student'";
                        
                        using (var cmd = new SqlCommand(countQuery, connection))
                        {
                            studentCount = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                    else
                    {
                        // Check if Students table exists
                        bool studentsExists = false;
                        string checkStudentsQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students'";
                        using (var cmd = new SqlCommand(checkStudentsQuery, connection))
                        {
                            int result = Convert.ToInt32(cmd.ExecuteScalar());
                            studentsExists = result > 0;
                        }
                        
                        if (studentsExists)
                        {
                            // Count from Students table
                            string countQuery = "SELECT COUNT(*) FROM Students";
                            using (var cmd = new SqlCommand(countQuery, connection))
                            {
                                studentCount = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue
                System.Diagnostics.Debug.WriteLine($"Error counting students: {ex.Message}");
            }
            
            ViewBag.TotalStudents = studentCount;
            return View();
        }

        public IActionResult ImportStudents()
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ImportStudents(IFormFile file)
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select an Excel file (.xlsx).";
                return View();
            }

            var list = new List<Student>();
            var successCount = 0;
            var errorCount = 0;
            var errors = new List<string>();

            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        var rowCount = worksheet.Dimension.Rows;

                        // Skip header row (row 1)
                        for (int row = 2; row <= rowCount; row++)
                        {
                            try
                            {
                                var student = new Student
                                {
                                    FullName = worksheet.Cells[row, 1].Value?.ToString()?.Trim(),
                                    Username = worksheet.Cells[row, 2].Value?.ToString()?.Trim(),
                                    Password = worksheet.Cells[row, 3].Value?.ToString()?.Trim(),
                                    IdNumber = worksheet.Cells[row, 4].Value?.ToString()?.Trim(),
                                    Course = worksheet.Cells[row, 5].Value?.ToString()?.Trim(),
                                    Section = worksheet.Cells[row, 6].Value?.ToString()?.Trim(),
                                    IsProfileVisible = true,
                                    IsResumeVisible = true,
                                    Score = 0,
                                    BadgeColor = "green"
                                };

                                // Validate required fields
                                if (string.IsNullOrEmpty(student.FullName) || 
                                    string.IsNullOrEmpty(student.Username) || 
                                    string.IsNullOrEmpty(student.Password) ||
                                    string.IsNullOrEmpty(student.IdNumber) || 
                                    string.IsNullOrEmpty(student.Course))
                                {
                                    errors.Add($"Row {row}: Missing required fields (Full Name, Username, Password, ID Number, or Course)");
                                    errorCount++;
                                    continue;
                                }

                                // Check if student with this ID already exists
                                if (_context.Students.Any(s => s.IdNumber == student.IdNumber))
                                {
                                    errors.Add($"Row {row}: Student with ID {student.IdNumber} already exists");
                                    errorCount++;
                                    continue;
                                }

                                // Check if username already exists
                                if (_context.Students.Any(s => s.Username == student.Username))
                                {
                                    errors.Add($"Row {row}: Username {student.Username} already exists");
                                    errorCount++;
                                    continue;
                                }

                                _context.Students.Add(student);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {row}: {ex.Message}");
                                errorCount++;
                            }
                        }

                        await _context.SaveChangesAsync();
                    }
                }

                TempData["Success"] = $"Successfully imported {successCount} student records.";
                if (errorCount > 0)
                {
                    TempData["ErrorList"] = string.Join("<br/>", errors);
                }

                return RedirectToAction(nameof(ImportStudents));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return View();
            }
        }

        public IActionResult Students()
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            var students = _context.Students.ToList();
            return View(students);
        }

        public IActionResult DownloadTemplate()
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            var stream = new MemoryStream();
            using (var package = new ExcelPackage(stream))
            {
                var worksheet = package.Workbook.Worksheets.Add("Students");
                
                // Add header row with all required columns
                worksheet.Cells[1, 1].Value = "Full Name";
                worksheet.Cells[1, 2].Value = "Username";
                worksheet.Cells[1, 3].Value = "Password";
                worksheet.Cells[1, 4].Value = "ID Number";
                worksheet.Cells[1, 5].Value = "Course";
                worksheet.Cells[1, 6].Value = "Section";
                
                // Format header row with bold font and background color
                using (var range = worksheet.Cells[1, 1, 1, 6])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Add sample data
                worksheet.Cells[2, 1].Value = "John Doe";
                worksheet.Cells[2, 2].Value = "john.doe";
                worksheet.Cells[2, 3].Value = "password123";
                worksheet.Cells[2, 4].Value = "2023001";
                worksheet.Cells[2, 5].Value = "Computer Science";
                worksheet.Cells[2, 6].Value = "A";
                
                worksheet.Cells[3, 1].Value = "Jane Smith";
                worksheet.Cells[3, 2].Value = "jane.smith";
                worksheet.Cells[3, 3].Value = "password456";
                worksheet.Cells[3, 4].Value = "2023002";
                worksheet.Cells[3, 5].Value = "Information Technology";
                worksheet.Cells[3, 6].Value = "B";
                
                // Auto-fit columns
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                
                package.Save();
            }
            
            stream.Position = 0;
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "StudentImportTemplate.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStudent(string id)
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return Json(new { success = false, message = "Unauthorized. You must be an admin to perform this action." });
            }

            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "Student ID is required" });
            }

            try
            {
                // Create connection string
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check which tables exist
                    bool studentsTableExists = false;
                    bool studentDetailsTableExists = false;
                    bool usersTableExists = false;
                    
                    // Check for Students table
                    string checkStudentsTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students'";
                    using (var cmd = new SqlCommand(checkStudentsTableQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        studentsTableExists = result != null && Convert.ToInt32(result) > 0;
                    }
                    
                    // Check for StudentDetails table
                    string checkDetailsTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'";
                    using (var cmd = new SqlCommand(checkDetailsTableQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        studentDetailsTableExists = result != null && Convert.ToInt32(result) > 0;
                    }
                    
                    // Check for Users table
                    string checkUsersTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users'";
                    using (var cmd = new SqlCommand(checkUsersTableQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        usersTableExists = result != null && Convert.ToInt32(result) > 0;
                    }
                    
                    // If no relevant tables exist, return error
                    if (!studentsTableExists && !studentDetailsTableExists)
                    {
                        return Json(new { success = false, message = "Neither Students nor StudentDetails tables exist. Unable to delete student." });
                    }
                    
                    // Begin a transaction for all database operations
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // First check if this student exists
                            bool studentExists = false;
                            string userId = null;
                            
                            // Check in StudentDetails table first (new structure)
                            if (studentDetailsTableExists)
                            {
                                string checkDetailsQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                                using (var cmd = new SqlCommand(checkDetailsQuery, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdNumber", id);
                                    var result = await cmd.ExecuteScalarAsync();
                                    if (result != null && result != DBNull.Value)
                                    {
                                        studentExists = true;
                                        userId = result.ToString();
                                    }
                                }
                            }
                            
                            // Check in Students table if not found and it exists
                            if (!studentExists && studentsTableExists)
                            {
                                string checkStudentQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                                using (var cmd = new SqlCommand(checkStudentQuery, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@IdNumber", id);
                                    var result = await cmd.ExecuteScalarAsync();
                                    studentExists = result != null && Convert.ToInt32(result) > 0;
                                }
                            }
                            
                            if (!studentExists)
                            {
                                return Json(new { success = false, message = "Student not found" });
                            }
                            
                            // DELETE FROM ALL RELATED TABLES IN CORRECT ORDER
                            // Start with child tables first to avoid foreign key constraints
                            
                            // 1. Delete from TestRecords
                            await ExecuteDeleteQuery(connection, transaction, "TestRecords", "StudentId", id, "test records");
                            
                            // 2. Delete from Certificates
                            await ExecuteDeleteQuery(connection, transaction, "Certificates", "StudentId", id, "certificates");
                            
                            // 3. Delete from ChallengeSubmissions
                            // This might use UserId instead of IdNumber
                            if (!string.IsNullOrEmpty(userId))
                            {
                                await ExecuteDeleteQuery(connection, transaction, "ChallengeSubmissions", "StudentId", userId, "challenge submissions");
                            }
                            else
                            {
                                await ExecuteDeleteQuery(connection, transaction, "ChallengeSubmissions", "StudentId", id, "challenge submissions");
                            }
                            
                            // 4. Delete from AttendanceRecords
                            await ExecuteDeleteQuery(connection, transaction, "AttendanceRecords", "StudentId", id, "attendance records");
                            if (!string.IsNullOrEmpty(userId))
                            {
                                await ExecuteDeleteQuery(connection, transaction, "AttendanceRecords", "StudentId", userId, "attendance records (by UserId)");
                            }
                            
                            // 5. Delete from ExtraCurricularActivities
                            await ExecuteDeleteQuery(connection, transaction, "ExtraCurricularActivities", "StudentId", id, "extracurricular activities");
                            if (!string.IsNullOrEmpty(userId))
                            {
                                await ExecuteDeleteQuery(connection, transaction, "ExtraCurricularActivities", "StudentId", userId, "extracurricular activities (by UserId)");
                            }
                            
                            // 6. Delete from VideoCalls
                            await ExecuteDeleteQuery(connection, transaction, "VideoCalls", "StudentId", id, "video calls");
                            if (!string.IsNullOrEmpty(userId))
                            {
                                await ExecuteDeleteQuery(connection, transaction, "VideoCalls", "StudentId", userId, "video calls (by UserId)");
                            }
                            
                            // 7. Delete from StudentDetails (if exists)
                            int detailsDeleted = studentDetailsTableExists ? 
                                await ExecuteDeleteQuery(connection, transaction, "StudentDetails", "IdNumber", id, "student details") : 0;
                            
                            // 8. Delete from Students table (if exists)
                            int studentsDeleted = studentsTableExists ? 
                                await ExecuteDeleteQuery(connection, transaction, "Students", "IdNumber", id, "student record") : 0;
                            
                            // 9. Delete from Users (if UserId was found and table exists)
                            int usersDeleted = 0;
                            if (!string.IsNullOrEmpty(userId) && usersTableExists)
                            {
                                usersDeleted = await ExecuteDeleteQuery(connection, transaction, "Users", "UserId", userId, "user account");
                            }
                            
                            // If we deleted from any main table, consider it a success
                            if (detailsDeleted > 0 || studentsDeleted > 0 || usersDeleted > 0)
                            {
                                transaction.Commit();
                                return Json(new { success = true, message = "Student and all associated data deleted successfully" });
                            }
                            else
                            {
                                transaction.Rollback();
                                return Json(new { success = false, message = "Could not delete the student record. No matching records found in any table." });
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return Json(new { success = false, message = $"Error deleting student: {ex.Message}" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper method to execute delete queries safely
        private async Task<int> ExecuteDeleteQuery(SqlConnection connection, SqlTransaction transaction, 
                                                  string tableName, string idColumnName, string idValue, string recordType)
        {
            try
            {
                // First check if the table exists
                string checkTableQuery = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
                using (var cmd = new SqlCommand(checkTableQuery, connection, transaction))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || Convert.ToInt32(result) == 0)
                    {
                        // Table doesn't exist, log this info and return 0
                        System.Diagnostics.Debug.WriteLine($"Table '{tableName}' does not exist, skipping deletion for {recordType}");
                        return 0;
                    }
                }
                
                // Table exists, now execute the delete
                string deleteQuery = $"DELETE FROM {tableName} WHERE {idColumnName} = @IdValue";
                using (var cmd = new SqlCommand(deleteQuery, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@IdValue", idValue);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync();
                    return rowsAffected;
                }
            }
            catch (Exception ex)
            {
                // Log the specific error but don't throw
                System.Diagnostics.Debug.WriteLine($"Error deleting from {tableName}: {ex.Message}");
                return 0;
            }
        }

        public IActionResult AdminDashboard()
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return RedirectToAction("Login", "Home");
            }
            
            // Get student count and details using direct SQL query, as we may not have a Students table
            List<Student> students = new List<Student>();
            int studentCount = 0;

            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    
                    // Check if StudentDetails table exists
                    bool studentDetailsExists = false;
                    string checkTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'";
                    using (var cmd = new SqlCommand(checkTableQuery, connection))
                    {
                        int result = Convert.ToInt32(cmd.ExecuteScalar());
                        studentDetailsExists = result > 0;
                    }

                    if (studentDetailsExists)
                    {
                        // Get students using the new table structure (StudentDetails + Users)
                        string countQuery = @"
                            SELECT COUNT(*) FROM StudentDetails sd
                            JOIN Users u ON sd.UserId = u.UserId
                            WHERE u.Role = 'student'";
                        
                        using (var cmd = new SqlCommand(countQuery, connection))
                        {
                            studentCount = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        
                        string query = @"
                            SELECT sd.IdNumber, u.FullName, sd.Course, sd.Section, sd.BadgeColor, sd.Score
                            FROM StudentDetails sd
                            JOIN Users u ON sd.UserId = u.UserId 
                            WHERE u.Role = 'student'
                            ORDER BY u.FullName";
                        
                        using (var cmd = new SqlCommand(query, connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                students.Add(new Student
                                {
                                    IdNumber = reader["IdNumber"]?.ToString(),
                                    FullName = reader["FullName"]?.ToString(),
                                    Course = reader["Course"]?.ToString(),
                                    Section = reader["Section"]?.ToString(),
                                    BadgeColor = reader["BadgeColor"]?.ToString(),
                                    Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0
                                });
                            }
                        }
                    }
                    else
                    {
                        // Check if Students table exists as a fallback
                        bool studentsExists = false;
                        string checkStudentsQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students'";
                        using (var cmd = new SqlCommand(checkStudentsQuery, connection))
                        {
                            int result = Convert.ToInt32(cmd.ExecuteScalar());
                            studentsExists = result > 0;
                        }
                        
                        if (studentsExists)
                        {
                            // Get data from Students table as a fallback
                            string countQuery = "SELECT COUNT(*) FROM Students";
                            using (var cmd = new SqlCommand(countQuery, connection))
                            {
                                studentCount = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                            
                            string query = "SELECT IdNumber, FullName, Course, Section, BadgeColor, Score FROM Students ORDER BY FullName";
                            using (var cmd = new SqlCommand(query, connection))
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    students.Add(new Student
                                    {
                                        IdNumber = reader["IdNumber"]?.ToString(),
                                        FullName = reader["FullName"]?.ToString(),
                                        Course = reader["Course"]?.ToString(),
                                        Section = reader["Section"]?.ToString(),
                                        BadgeColor = reader["BadgeColor"]?.ToString(),
                                        Score = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with empty list
                System.Diagnostics.Debug.WriteLine($"Error loading students: {ex.Message}");
            }
            
            ViewBag.TotalStudents = studentCount;
            return View(students);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(string IdNumber, string FullName, string Course, string Section, int Score)
        {
            // Check if user is authenticated as admin using session
            if (HttpContext.Session.GetString("Role") != "admin")
            {
                return Json(new { success = false, message = "Unauthorized. You must be an admin to perform this action." });
            }
            
            if (string.IsNullOrEmpty(IdNumber))
            {
                return Json(new { success = false, message = "Student ID is required" });
            }

            try
            {
                string connectionString = _configuration.GetConnectionString("DefaultConnection");
                bool updated = false;
                string badgeColor = GetBadgeColorFromScore(Score);
                
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check which tables exist
                    bool studentsTableExists = false;
                    bool studentDetailsTableExists = false;
                    
                    // Check for Students table
                    string checkStudentsTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Students'";
                    using (var cmd = new SqlCommand(checkStudentsTableQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        studentsTableExists = result != null && Convert.ToInt32(result) > 0;
                    }
                    
                    // Check for StudentDetails table
                    string checkDetailsTableQuery = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StudentDetails'";
                    using (var cmd = new SqlCommand(checkDetailsTableQuery, connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        studentDetailsTableExists = result != null && Convert.ToInt32(result) > 0;
                    }
                    
                    // Try updating StudentDetails table first
                    if (studentDetailsTableExists)
                    {
                        string updateQuery = @"
                            UPDATE StudentDetails 
                            SET Course = @Course, Section = @Section, Score = @Score, BadgeColor = @BadgeColor
                            WHERE IdNumber = @IdNumber;
                            SELECT @@ROWCOUNT;";
                            
                        using (var cmd = new SqlCommand(updateQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                            cmd.Parameters.AddWithValue("@Course", Course);
                            cmd.Parameters.AddWithValue("@Section", Section);
                            cmd.Parameters.AddWithValue("@Score", Score);
                            cmd.Parameters.AddWithValue("@BadgeColor", badgeColor);
                            
                            int rowsAffected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            
                            if (rowsAffected > 0)
                            {
                                // Also update the name in Users table if needed
                                string updateUserQuery = @"
                                    UPDATE Users
                                    SET FullName = @FullName
                                    FROM Users u
                                    JOIN StudentDetails sd ON u.UserId = sd.UserId
                                    WHERE sd.IdNumber = @IdNumber;";
                                    
                                using (var userCmd = new SqlCommand(updateUserQuery, connection))
                                {
                                    userCmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                                    userCmd.Parameters.AddWithValue("@FullName", FullName);
                                    await userCmd.ExecuteNonQueryAsync();
                                }
                                
                                updated = true;
                            }
                        }
                    }
                    
                    // If StudentDetails update failed or didn't exist, try updating Students table
                    if (!updated && studentsTableExists)
                    {
                        string updateQuery = @"
                            UPDATE Students 
                            SET FullName = @FullName, Course = @Course, Section = @Section, 
                                Score = @Score, BadgeColor = @BadgeColor
                            WHERE IdNumber = @IdNumber;
                            SELECT @@ROWCOUNT;";
                            
                        using (var cmd = new SqlCommand(updateQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@IdNumber", IdNumber);
                            cmd.Parameters.AddWithValue("@FullName", FullName);
                            cmd.Parameters.AddWithValue("@Course", Course);
                            cmd.Parameters.AddWithValue("@Section", Section);
                            cmd.Parameters.AddWithValue("@Score", Score);
                            cmd.Parameters.AddWithValue("@BadgeColor", badgeColor);
                            
                            int rowsAffected = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            updated = rowsAffected > 0;
                        }
                    }
                }
                
                if (updated)
                {
                    return Json(new { success = true });
                }
                else
                {
                    return Json(new { success = false, message = "Student not found or update failed" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to determine badge color based on score
        private string GetBadgeColorFromScore(int score)
        {
            if (score >= 90) return "gold";
            if (score >= 80) return "silver";
            if (score >= 70) return "bronze";
            return "green";
        }

        [HttpGet]
        [Route("CreateAdmin")]
        public async Task<IActionResult> CreateAdmin()
        {
            try
            {
                // Only allow existing admin users to create another admin
                string role = HttpContext.Session.GetString("Role");
                if (role != "admin")
                {
                    return Json(new { success = false, message = "Unauthorized access" });
                }

                // Create the admin user
                await CreateAdminUser.CreateAdmin(_configuration);

                return Json(new { success = true, message = "Admin user created successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
} 