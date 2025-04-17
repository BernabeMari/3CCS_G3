using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Collections.Generic;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
// Other using statements...

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ImportExportController : Controller
    {
        private readonly string _connectionString;

        public ImportExportController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost("ImportStudents")]
        public async Task<IActionResult> ImportStudents(IFormFile file)
        {
            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select an Excel file (.xlsx).";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            var successCount = 0;
            var errorCount = 0;
            var errors = new List<string>();

            try
            {
                // Make sure to register the EPPlus license context at application startup
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        var rowCount = worksheet.Dimension.Rows;
                        var colCount = worksheet.Dimension.Columns;

                        // Check if a password column exists in the header row
                        bool hasPasswordColumn = false;
                        Dictionary<string, int> columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        
                        // Map column names to column indices
                        for (int col = 1; col <= colCount; col++)
                        {
                            string columnName = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                            if (columnName != null)
                            {
                                // Remove any * from column names (used to mark required fields in template)
                                columnName = columnName.Replace("*", "").Trim();
                                
                                columnMap[columnName] = col;
                                
                                if (columnName.Equals("Password", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasPasswordColumn = true;
                                }
                            }
                        }

                        if (hasPasswordColumn)
                        {
                            TempData["Error"] = "For security reasons, the spreadsheet cannot contain a Password column. Please remove the Password column and try again.";
                            return RedirectToAction("AdminDashboard", "Dashboard");
                        }

                        // Check for required columns
                        string[] requiredColumns = { "Full Name", "Username", "ID Number", "Course" };
                        foreach (var column in requiredColumns)
                        {
                            if (!columnMap.ContainsKey(column))
                            {
                                TempData["Error"] = $"Required column '{column}' is missing in the uploaded file. Please use the template provided.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }

                        using (var connection = new SqlConnection(_connectionString))
                        {
                            await connection.OpenAsync();

                            // Check if we're using the new database structure
                            bool usingNewTables = false;
                            string checkTableQuery = @"
                                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                WHERE TABLE_NAME = 'StudentDetails'";
                                
                            using (var command = new SqlCommand(checkTableQuery, connection))
                            {
                                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                usingNewTables = (count > 0);
                            }

                            // Skip header row (row 1)
                            for (int row = 2; row <= rowCount; row++)
                            {
                                try
                                {
                                    // Get data from mapped columns
                                    string fullName = worksheet.Cells[row, columnMap["Full Name"]].Value?.ToString()?.Trim();
                                    string username = worksheet.Cells[row, columnMap["Username"]].Value?.ToString()?.Trim();
                                    string idNumber = worksheet.Cells[row, columnMap["ID Number"]].Value?.ToString()?.Trim();
                                    string course = worksheet.Cells[row, columnMap["Course"]].Value?.ToString()?.Trim();
                                    
                                    // Optional columns
                                    string section = columnMap.ContainsKey("Section") ? 
                                        worksheet.Cells[row, columnMap["Section"]].Value?.ToString()?.Trim() : null;
                                    string email = columnMap.ContainsKey("Email") ? 
                                        worksheet.Cells[row, columnMap["Email"]].Value?.ToString()?.Trim() : null;
                                    string phoneNumber = columnMap.ContainsKey("Phone Number") ? 
                                        worksheet.Cells[row, columnMap["Phone Number"]].Value?.ToString()?.Trim() : null;
                                    
                                    // Instead of generating a random password, set it to null and flag for password change
                                    string password = "";

                                    // Validate required fields
                                    if (string.IsNullOrEmpty(fullName) || 
                                        string.IsNullOrEmpty(username) || 
                                        string.IsNullOrEmpty(idNumber) || 
                                        string.IsNullOrEmpty(course))
                                    {
                                        errors.Add($"Row {row}: Missing required fields (Full Name, Username, ID Number, or Course)");
                                        errorCount++;
                                        continue;
                                    }

                                    if (usingNewTables)
                                    {
                                        // Check if username already exists in Users table
                                        string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@Username", username);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Username {username} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Check if ID number already exists in StudentDetails table
                                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @IdNumber";
                                        using (var command = new SqlCommand(checkIdQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Student with ID {idNumber} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Make sure NeedsPasswordChange column exists in Users table
                                        bool hasNeedsPasswordChangeColumn = false;
                                        string checkColumnQuery = @"
                                            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                                            WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'NeedsPasswordChange'";
                                        
                                        using (var command = new SqlCommand(checkColumnQuery, connection))
                                        {
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            hasNeedsPasswordChangeColumn = (count > 0);
                                        }
                                        
                                        // Add NeedsPasswordChange column if it doesn't exist
                                        if (!hasNeedsPasswordChangeColumn)
                                        {
                                            string addColumnQuery = "ALTER TABLE Users ADD NeedsPasswordChange BIT NOT NULL DEFAULT 0";
                                            using (var command = new SqlCommand(addColumnQuery, connection))
                                            {
                                                await command.ExecuteNonQueryAsync();
                                            }
                                        }

                                        // Insert into Users table with NeedsPasswordChange flag
                                        string userId = Guid.NewGuid().ToString();
                                        string insertUserQuery = @"
                                            INSERT INTO Users (UserId, FullName, Username, Password, Role, Email, PhoneNumber, NeedsPasswordChange, IsVerified) 
                                            VALUES (@UserId, @FullName, @Username, @Password, 'student', @Email, @PhoneNumber, 1, 0)";
                                        
                                        using (var command = new SqlCommand(insertUserQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@UserId", userId);
                                            command.Parameters.AddWithValue("@FullName", fullName);
                                            command.Parameters.AddWithValue("@Username", username);
                                            command.Parameters.AddWithValue("@Password", password);
                                            command.Parameters.AddWithValue("@Email", email ?? (object)DBNull.Value);
                                            command.Parameters.AddWithValue("@PhoneNumber", phoneNumber ?? (object)DBNull.Value);
                                            
                                            await command.ExecuteNonQueryAsync();
                                        }

                                        // Insert into StudentDetails table
                                        string insertStudentQuery = @"
                                            INSERT INTO StudentDetails (UserId, IdNumber, Course, Section, Score, BadgeColor, IsProfileVisible, IsResumeVisible)
                                            VALUES (@UserId, @IdNumber, @Course, @Section, 0, 'green', 1, 1)";
                                        
                                        using (var command = new SqlCommand(insertStudentQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@UserId", userId);
                                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                                            command.Parameters.AddWithValue("@Course", course);
                                            command.Parameters.AddWithValue("@Section", section ?? "");
                                            await command.ExecuteNonQueryAsync();
                                        }
                                    }
                                    else
                                    {
                                        // Using old Students table
                                        
                                        // Check if student with this ID already exists
                                        string checkQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                                        using (var command = new SqlCommand(checkQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Student with ID {idNumber} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Check if username already exists
                                        string checkUsernameQuery = "SELECT COUNT(*) FROM Students WHERE Username = @Username";
                                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@Username", username);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Username {username} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Check if NeedsPasswordChange column exists in Students table
                                        bool hasNeedsPasswordChangeColumn = false;
                                        string checkColumnQuery = @"
                                            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                                            WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'NeedsPasswordChange'";
                                        
                                        using (var command = new SqlCommand(checkColumnQuery, connection))
                                        {
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            hasNeedsPasswordChangeColumn = (count > 0);
                                        }
                                        
                                        // Add NeedsPasswordChange column if it doesn't exist
                                        if (!hasNeedsPasswordChangeColumn)
                                        {
                                            string addColumnQuery = "ALTER TABLE Students ADD NeedsPasswordChange BIT NOT NULL DEFAULT 0";
                                            using (var command = new SqlCommand(addColumnQuery, connection))
                                            {
                                                await command.ExecuteNonQueryAsync();
                                            }
                                        }

                                        // Insert into Students table
                                        string insertQuery = hasNeedsPasswordChangeColumn
                                            ? @"INSERT INTO Students (IdNumber, FullName, Username, Password, Course, Section, Score, BadgeColor, IsProfileVisible, IsResumeVisible, NeedsPasswordChange)
                                                VALUES (@IdNumber, @FullName, @Username, @Password, @Course, @Section, 0, 'green', 1, 1, 1)"
                                            : @"INSERT INTO Students (IdNumber, FullName, Username, Password, Course, Section, Score, BadgeColor, IsProfileVisible, IsResumeVisible)
                                                VALUES (@IdNumber, @FullName, @Username, @Password, @Course, @Section, 0, 'green', 1, 1)";
                                        
                                        using (var command = new SqlCommand(insertQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                                            command.Parameters.AddWithValue("@FullName", fullName);
                                            command.Parameters.AddWithValue("@Username", username);
                                            command.Parameters.AddWithValue("@Password", password);
                                            command.Parameters.AddWithValue("@Course", course);
                                            command.Parameters.AddWithValue("@Section", section ?? "");
                                            await command.ExecuteNonQueryAsync();
                                        }
                                    }

                                    successCount++;
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Row {row}: {ex.Message}");
                                    errorCount++;
                                }
                            }
                        }
                    }
                }

                TempData["Success"] = $"Successfully imported {successCount} student records.";
                if (errorCount > 0)
                {
                    TempData["ErrorList"] = string.Join("<br/>", errors);
                }

                return RedirectToAction("AdminDashboard", "Dashboard");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error importing students: {ex.Message}";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
        }

        [HttpPost("ImportTeachers")]
        public async Task<IActionResult> ImportTeachers(IFormFile file)
        {
            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select an Excel file (.xlsx).";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            var successCount = 0;
            var errorCount = 0;
            var errors = new List<string>();

            try
            {
                // Make sure to register the EPPlus license context at application startup
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        var rowCount = worksheet.Dimension.Rows;
                        var colCount = worksheet.Dimension.Columns;

                        // Check if a password column exists in the header row
                        bool hasPasswordColumn = false;
                        Dictionary<string, int> columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        
                        // Map column names to column indices
                        for (int col = 1; col <= colCount; col++)
                        {
                            string columnName = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                            if (columnName != null)
                            {
                                // Remove any * from column names (used to mark required fields in template)
                                columnName = columnName.Replace("*", "").Trim();
                                
                                columnMap[columnName] = col;
                                
                                if (columnName.Equals("Password", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasPasswordColumn = true;
                                }
                            }
                        }

                        if (hasPasswordColumn)
                        {
                            TempData["Error"] = "For security reasons, the spreadsheet cannot contain a Password column. Please remove the Password column and try again.";
                            return RedirectToAction("AdminDashboard", "Dashboard");
                        }

                        // Check for required columns
                        string[] requiredColumns = { "Full Name", "Username", "Department", "Position" };
                        foreach (var column in requiredColumns)
                        {
                            if (!columnMap.ContainsKey(column))
                            {
                                TempData["Error"] = $"Required column '{column}' is missing in the uploaded file. Please use the template provided.";
                                return RedirectToAction("AdminDashboard", "Dashboard");
                            }
                        }

                        using (var connection = new SqlConnection(_connectionString))
                        {
                            await connection.OpenAsync();

                            // Check if we're using the new database structure
                            bool usingNewTables = false;
                            string checkTableQuery = @"
                                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                WHERE TABLE_NAME = 'TeacherDetails'";
                                
                            using (var command = new SqlCommand(checkTableQuery, connection))
                            {
                                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                usingNewTables = (count > 0);
                            }

                            // Skip header row (row 1)
                            for (int row = 2; row <= rowCount; row++)
                            {
                                try
                                {
                                    // Get data from mapped columns
                                    string fullName = worksheet.Cells[row, columnMap["Full Name"]].Value?.ToString()?.Trim();
                                    string username = worksheet.Cells[row, columnMap["Username"]].Value?.ToString()?.Trim();
                                    string department = worksheet.Cells[row, columnMap["Department"]].Value?.ToString()?.Trim();
                                    string position = worksheet.Cells[row, columnMap["Position"]].Value?.ToString()?.Trim();
                                    
                                    // Instead of generating a random password, set it to null and flag for password change
                                    string password = "";

                                    // Validate required fields
                                    if (string.IsNullOrEmpty(fullName) || 
                                        string.IsNullOrEmpty(username) || 
                                        string.IsNullOrEmpty(department) || 
                                        string.IsNullOrEmpty(position))
                                    {
                                        errors.Add($"Row {row}: Missing required fields (Full Name, Username, Department, or Position)");
                                        errorCount++;
                                        continue;
                                    }

                                    if (usingNewTables)
                                    {
                                        // Check if username already exists in Users table
                                        string checkUsernameQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@Username", username);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Username {username} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Insert into Users table
                                        string userId = Guid.NewGuid().ToString();
                                        string insertUserQuery = @"
                                            INSERT INTO Users (UserId, FullName, Username, Password, Role, NeedsPasswordChange, IsVerified) 
                                            VALUES (@UserId, @FullName, @Username, @Password, 'teacher', 1, 0)";
                                        
                                        using (var command = new SqlCommand(insertUserQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@UserId", userId);
                                            command.Parameters.AddWithValue("@FullName", fullName);
                                            command.Parameters.AddWithValue("@Username", username);
                                            command.Parameters.AddWithValue("@Password", password);
                                            
                                            await command.ExecuteNonQueryAsync();
                                        }

                                        // Insert into TeacherDetails table
                                        string insertTeacherQuery = @"
                                            INSERT INTO TeacherDetails (UserId, Department, Position)
                                            VALUES (@UserId, @Department, @Position)";
                                        
                                        using (var command = new SqlCommand(insertTeacherQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@UserId", userId);
                                            command.Parameters.AddWithValue("@Department", department);
                                            command.Parameters.AddWithValue("@Position", position);
                                            await command.ExecuteNonQueryAsync();
                                        }
                                    }
                                    else
                                    {
                                        // Check if Teachers table exists
                                        bool teachersTableExists = false;
                                        string checkTeachersTableQuery = @"
                                            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                            WHERE TABLE_NAME = 'Teachers'";
                                            
                                        using (var command = new SqlCommand(checkTeachersTableQuery, connection))
                                        {
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            teachersTableExists = (count > 0);
                                        }
                                        
                                        if (!teachersTableExists)
                                        {
                                            // Create Teachers table if it doesn't exist
                                            string createTeachersTableQuery = @"
                                                CREATE TABLE Teachers (
                                                    TeacherId VARCHAR(50) PRIMARY KEY,
                                                    FullName NVARCHAR(100) NOT NULL,
                                                    Username NVARCHAR(50) NOT NULL UNIQUE,
                                                    Password NVARCHAR(50) NULL,
                                                    Department NVARCHAR(100) NOT NULL,
                                                    Position NVARCHAR(100) NOT NULL,
                                                    NeedsPasswordChange BIT NOT NULL DEFAULT 1
                                                )";
                                                
                                            using (var command = new SqlCommand(createTeachersTableQuery, connection))
                                            {
                                                await command.ExecuteNonQueryAsync();
                                            }
                                        }
                                        else
                                        {
                                            // Check if NeedsPasswordChange column exists in Teachers table
                                            bool hasNeedsPasswordChangeColumn = false;
                                            string checkColumnQuery = @"
                                                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                                                WHERE TABLE_NAME = 'Teachers' AND COLUMN_NAME = 'NeedsPasswordChange'";
                                            
                                            using (var command = new SqlCommand(checkColumnQuery, connection))
                                            {
                                                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                                hasNeedsPasswordChangeColumn = (count > 0);
                                            }
                                            
                                            // Add NeedsPasswordChange column if it doesn't exist
                                            if (!hasNeedsPasswordChangeColumn)
                                            {
                                                string addColumnQuery = "ALTER TABLE Teachers ADD NeedsPasswordChange BIT NOT NULL DEFAULT 0";
                                                using (var command = new SqlCommand(addColumnQuery, connection))
                                                {
                                                    await command.ExecuteNonQueryAsync();
                                                }
                                            }
                                        }

                                        // Check if username already exists
                                        string checkUsernameQuery = "SELECT COUNT(*) FROM Teachers WHERE Username = @Username";
                                        using (var command = new SqlCommand(checkUsernameQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@Username", username);
                                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                            
                                            if (count > 0)
                                            {
                                                errors.Add($"Row {row}: Username {username} already exists");
                                                errorCount++;
                                                continue;
                                            }
                                        }

                                        // Insert into Teachers table
                                        string teacherId = Guid.NewGuid().ToString();
                                        string insertQuery = @"
                                            INSERT INTO Teachers (TeacherId, FullName, Username, Password, Department, Position, NeedsPasswordChange)
                                            VALUES (@TeacherId, @FullName, @Username, @Password, @Department, @Position, 1)";
                                        
                                        using (var command = new SqlCommand(insertQuery, connection))
                                        {
                                            command.Parameters.AddWithValue("@TeacherId", teacherId);
                                            command.Parameters.AddWithValue("@FullName", fullName);
                                            command.Parameters.AddWithValue("@Username", username);
                                            command.Parameters.AddWithValue("@Password", password);
                                            command.Parameters.AddWithValue("@Department", department);
                                            command.Parameters.AddWithValue("@Position", position);
                                            
                                            await command.ExecuteNonQueryAsync();
                                        }
                                    }

                                    successCount++;
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"Row {row}: {ex.Message}");
                                    errorCount++;
                                }
                            }
                        }
                    }
                }

                TempData["Success"] = $"Successfully imported {successCount} teacher records.";
                if (errorCount > 0)
                {
                    TempData["ErrorList"] = string.Join("<br/>", errors);
                }

                return RedirectToAction("AdminDashboard", "Dashboard");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error importing teachers: {ex.Message}";
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
        }

        [HttpGet("DownloadStudentTemplate")]
        public IActionResult DownloadTemplate()
        {
            // Make sure to register the EPPlus license context at application startup
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            
            var stream = new MemoryStream();
            using (var package = new ExcelPackage(stream))
            {
                var worksheet = package.Workbook.Worksheets.Add("Students");
                
                // Add header row with all required columns
                worksheet.Cells[1, 1].Value = "Full Name*";
                worksheet.Cells[1, 2].Value = "Username*";
                worksheet.Cells[1, 3].Value = "ID Number*";
                worksheet.Cells[1, 4].Value = "Course*";
                worksheet.Cells[1, 5].Value = "Section*";
                worksheet.Cells[1, 6].Value = "Email";
                worksheet.Cells[1, 7].Value = "Phone Number";
                
                // Format header row with bold font and background color
                using (var range = worksheet.Cells[1, 1, 1, 7])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Add sample data
                worksheet.Cells[2, 1].Value = "Mari John Robert M.Bernabe";
                worksheet.Cells[2, 2].Value = "21-03000";
                worksheet.Cells[2, 3].Value = "21-03000";
                worksheet.Cells[2, 4].Value = "CICT";
                worksheet.Cells[2, 5].Value = "C2022";
                worksheet.Cells[2, 6].Value = "mari123@gmail.com";
                worksheet.Cells[2, 7].Value = "09123456789";
                
                worksheet.Cells[3, 1].Value = "Joy Bantule";
                worksheet.Cells[3, 2].Value = "21-03002";
                worksheet.Cells[3, 3].Value = "21-03002";
                worksheet.Cells[3, 4].Value = "CICT";
                worksheet.Cells[3, 5].Value = "B2022";
                worksheet.Cells[3, 6].Value = "joy.joy@gmail.com";
                worksheet.Cells[3, 7].Value = "09987654321";
                
                // Add a note about valid Course values
                worksheet.Cells[5, 1].Value = "* All fields are required except Phone Number";
                worksheet.Cells[5, 1, 5, 7].Merge = true;
                worksheet.Cells[5, 1].Style.Font.Bold = true;
                worksheet.Cells[5, 1].Style.Font.Italic = true;
                
                worksheet.Cells[6, 1].Value = "Note: After account verification, the user will be prompted to set their password.";
                worksheet.Cells[6, 1, 6, 7].Merge = true;
                worksheet.Cells[6, 1].Style.Font.Bold = true;
                worksheet.Cells[6, 1].Style.Font.Italic = true;
                
                worksheet.Cells[7, 1, 7, 7].Merge = true;
                worksheet.Cells[7, 1].Style.Font.Italic = true;
                
                // Auto-fit columns
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                
                package.Save();
            }
            
            stream.Position = 0;
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "StudentImportTemplate.xlsx");
        }

        // Add this method for downloading the teacher import template
        [HttpGet("DownloadTeacherTemplate")]
        public IActionResult DownloadTeacherTemplate()
        {
            // Make sure to register the EPPlus license context at application startup
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            
            var stream = new MemoryStream();
            using (var package = new ExcelPackage(stream))
            {
                var worksheet = package.Workbook.Worksheets.Add("Teachers");
                
                // Add header row with all required columns
                worksheet.Cells[1, 1].Value = "Full Name*";
                worksheet.Cells[1, 2].Value = "Username*";
                worksheet.Cells[1, 3].Value = "Department*";
                worksheet.Cells[1, 4].Value = "Position*";
                worksheet.Cells[1, 5].Value = "Email";
                
                // Format header row with bold font and background color
                using (var range = worksheet.Cells[1, 1, 1, 5])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Add sample data
                worksheet.Cells[2, 1].Value = "Mari John Robert M.Bernabe";
                worksheet.Cells[2, 2].Value = "TCH1";
                worksheet.Cells[2, 3].Value = "CICT";
                worksheet.Cells[2, 4].Value = "Professor";
                worksheet.Cells[2, 5].Value = "mari123@gmail.com";
                
                worksheet.Cells[3, 1].Value = "Ryan Encarnacion";
                worksheet.Cells[3, 2].Value = "TCH2";
                worksheet.Cells[3, 3].Value = "CICT";
                worksheet.Cells[3, 4].Value = "Assistant Professor";
                worksheet.Cells[3, 5].Value = "ryan@gmail.com";
                
                // Add a note about all fields being required
                worksheet.Cells[5, 1].Value = "* All fields are required";
                worksheet.Cells[5, 1, 5, 5].Merge = true;
                worksheet.Cells[5, 1].Style.Font.Bold = true;
                worksheet.Cells[5, 1].Style.Font.Italic = true;
                
                worksheet.Cells[6, 1].Value = "Note: After account verification, the user will be prompted to set their password.";
                worksheet.Cells[6, 1, 6, 5].Merge = true;
                worksheet.Cells[6, 1].Style.Font.Bold = true;
                worksheet.Cells[6, 1].Style.Font.Italic = true;
                
                // Auto-fit columns
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                
                package.Save();
            }
            
            stream.Position = 0;
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "TeacherImportTemplate.xlsx");
        }
    }
}
