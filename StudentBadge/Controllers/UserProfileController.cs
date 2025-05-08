using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using StudentBadge.Services;
using System.Linq;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class UserProfileController : Controller
    {
        private readonly ILogger<UserProfileController> _logger;
        private readonly string _connectionString;

        public UserProfileController(ILogger<UserProfileController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GetUserProfile")]
        public async Task<IActionResult> GetUserProfile()
        {
            try
            {
                string userId = HttpContext.Session.GetString("UserId");
                string role = HttpContext.Session.GetString("Role");
                
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
                {
                    return Json(new { success = false, message = "User not logged in." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if we're using the new schema
                    bool usersTableExists = await TableExists(connection, "Users");
                    
                    if (!usersTableExists)
                    {
                        return Json(new { success = false, message = "Users table does not exist." });
                    }
                    
                    dynamic profile = new ExpandoObject();
                    
                    // Get basic user info from Users table
                    string baseQuery = @"
                        SELECT 
                            UserId,
                            Username,
                            FullName,
                            Email,
                            Role
                        FROM 
                            Users
                        WHERE 
                            UserId = @UserId";
                    
                    using (var command = new SqlCommand(baseQuery, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                profile.userId = reader["UserId"].ToString();
                                profile.username = reader["Username"].ToString();
                                profile.fullName = reader["FullName"].ToString();
                                profile.email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString();
                                profile.role = reader["Role"].ToString();
                            }
                            else
                            {
                                return Json(new { success = false, message = "User not found." });
                            }
                        }
                    }
                    
                    // Get additional details based on role
                    if (role.Equals("student", StringComparison.OrdinalIgnoreCase))
                    {
                        await GetStudentProfileDetails(connection, userId, profile);
                    }
                    else if (role.Equals("teacher", StringComparison.OrdinalIgnoreCase))
                    {
                        await GetTeacherProfileDetails(connection, userId, profile);
                    }
                    else if (role.Equals("employer", StringComparison.OrdinalIgnoreCase))
                    {
                        await GetEmployerProfileDetails(connection, userId, profile);
                    }
                    
                    return Json(new { success = true, profile = profile });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user profile");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task GetStudentProfileDetails(SqlConnection connection, string userId, dynamic profile)
        {
            string query = @"
                SELECT 
                    IdNumber,
                    Course,
                    Section,
                    YearLevel,
                    PhotoUrl,
                    Address,
                    ContactNumber,
                    GuardianName,
                    GuardianContact
                FROM 
                    StudentDetails
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        profile.idNumber = reader["IdNumber"].ToString();
                        profile.course = reader.IsDBNull(reader.GetOrdinal("Course")) ? null : reader["Course"].ToString();
                        profile.section = reader.IsDBNull(reader.GetOrdinal("Section")) ? null : reader["Section"].ToString();
                        profile.yearLevel = reader.IsDBNull(reader.GetOrdinal("YearLevel")) ? (int?)null : Convert.ToInt32(reader["YearLevel"]);
                        profile.photoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader["PhotoUrl"].ToString();
                        profile.address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString();
                        profile.contactNumber = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader["ContactNumber"].ToString();
                        profile.guardianName = reader.IsDBNull(reader.GetOrdinal("GuardianName")) ? null : reader["GuardianName"].ToString();
                        profile.guardianContact = reader.IsDBNull(reader.GetOrdinal("GuardianContact")) ? null : reader["GuardianContact"].ToString();
                    }
                }
            }
        }
        
        private async Task GetTeacherProfileDetails(SqlConnection connection, string userId, dynamic profile)
        {
            string query = @"
                SELECT 
                    TeacherId,
                    Department,
                    Specialization,
                    PhotoUrl,
                    ContactNumber,
                    Address
                FROM 
                    TeacherDetails
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        profile.teacherId = reader["TeacherId"].ToString();
                        profile.department = reader.IsDBNull(reader.GetOrdinal("Department")) ? null : reader["Department"].ToString();
                        profile.specialization = reader.IsDBNull(reader.GetOrdinal("Specialization")) ? null : reader["Specialization"].ToString();
                        profile.photoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader["PhotoUrl"].ToString();
                        profile.contactNumber = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader["ContactNumber"].ToString();
                        profile.address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString();
                    }
                }
            }
        }
        
        private async Task GetEmployerProfileDetails(SqlConnection connection, string userId, dynamic profile)
        {
            string query = @"
                SELECT 
                    EmployerId,
                    Company,
                    Industry,
                    Position,
                    PhoneNumber,
                    Address,
                    CompanyLogoUrl
                FROM 
                    EmployerDetails
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        profile.employerId = reader["EmployerId"].ToString();
                        profile.company = reader.IsDBNull(reader.GetOrdinal("Company")) ? null : reader["Company"].ToString();
                        profile.industry = reader.IsDBNull(reader.GetOrdinal("Industry")) ? null : reader["Industry"].ToString();
                        profile.position = reader.IsDBNull(reader.GetOrdinal("Position")) ? null : reader["Position"].ToString();
                        profile.phoneNumber = reader.IsDBNull(reader.GetOrdinal("PhoneNumber")) ? null : reader["PhoneNumber"].ToString();
                        profile.address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString();
                        profile.companyLogoUrl = reader.IsDBNull(reader.GetOrdinal("CompanyLogoUrl")) ? null : reader["CompanyLogoUrl"].ToString();
                    }
                }
            }
        }
        
        [HttpPost]
        [Route("UpdateUserProfile")]
        public async Task<IActionResult> UpdateUserProfile()
        {
            try
            {
                string userId = HttpContext.Session.GetString("UserId");
                string role = HttpContext.Session.GetString("Role");
                
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
                {
                    return Json(new { success = false, message = "User not logged in." });
                }
                
                // Get form values
                string fullName = Request.Form["fullName"];
                string email = Request.Form["email"];
                
                if (string.IsNullOrEmpty(fullName))
                {
                    return Json(new { success = false, message = "Full name is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Begin transaction for all updates
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Update basic user info in Users table
                            string baseUpdateQuery = @"
                                UPDATE Users 
                                SET 
                                    FullName = @FullName, 
                                    Email = @Email
                                WHERE 
                                    UserId = @UserId";
                            
                            using (var command = new SqlCommand(baseUpdateQuery, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@FullName", fullName);
                                command.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(email) ? (object)DBNull.Value : email);
                                command.Parameters.AddWithValue("@UserId", userId);
                                
                                await command.ExecuteNonQueryAsync();
                            }
                            
                            // Update role-specific details
                            if (role.Equals("student", StringComparison.OrdinalIgnoreCase))
                            {
                                await UpdateStudentDetails(connection, transaction, userId);
                            }
                            else if (role.Equals("teacher", StringComparison.OrdinalIgnoreCase))
                            {
                                await UpdateTeacherDetails(connection, transaction, userId);
                            }
                            else if (role.Equals("employer", StringComparison.OrdinalIgnoreCase))
                            {
                                await UpdateEmployerDetails(connection, transaction, userId);
                            }
                            
                            // Process profile photo upload if exists
                            if (Request.Form.Files.Count > 0 && Request.Form.Files[0] != null && Request.Form.Files[0].Length > 0)
                            {
                                string photoUrl = await SaveProfilePhoto(Request.Form.Files[0], role, userId);
                                
                                if (!string.IsNullOrEmpty(photoUrl))
                                {
                                    await UpdateProfilePhoto(connection, transaction, role, userId, photoUrl);
                                }
                            }
                            
                            // Commit all changes
                            transaction.Commit();
                            
                            // Update session values
                            HttpContext.Session.SetString("FullName", fullName);
                            
                            return Json(new { success = true, message = "Profile updated successfully." });
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw ex;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user profile");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task UpdateStudentDetails(SqlConnection connection, SqlTransaction transaction, string userId)
        {
            string course = Request.Form["course"];
            string section = Request.Form["section"];
            string yearLevelStr = Request.Form["yearLevel"];
            string address = Request.Form["address"];
            string contactNumber = Request.Form["contactNumber"];
            string guardianName = Request.Form["guardianName"];
            string guardianContact = Request.Form["guardianContact"];
            
            int? yearLevel = null;
            if (!string.IsNullOrEmpty(yearLevelStr) && int.TryParse(yearLevelStr, out int parsedYearLevel))
            {
                yearLevel = parsedYearLevel;
            }
            
            string query = @"
                UPDATE StudentDetails 
                SET 
                    Course = @Course,
                    Section = @Section,
                    YearLevel = @YearLevel,
                    Address = @Address,
                    ContactNumber = @ContactNumber,
                    GuardianName = @GuardianName,
                    GuardianContact = @GuardianContact
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@Course", string.IsNullOrEmpty(course) ? (object)DBNull.Value : course);
                command.Parameters.AddWithValue("@Section", string.IsNullOrEmpty(section) ? (object)DBNull.Value : section);
                command.Parameters.AddWithValue("@YearLevel", yearLevel.HasValue ? (object)yearLevel.Value : DBNull.Value);
                command.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(address) ? (object)DBNull.Value : address);
                command.Parameters.AddWithValue("@ContactNumber", string.IsNullOrEmpty(contactNumber) ? (object)DBNull.Value : contactNumber);
                command.Parameters.AddWithValue("@GuardianName", string.IsNullOrEmpty(guardianName) ? (object)DBNull.Value : guardianName);
                command.Parameters.AddWithValue("@GuardianContact", string.IsNullOrEmpty(guardianContact) ? (object)DBNull.Value : guardianContact);
                command.Parameters.AddWithValue("@UserId", userId);
                
                await command.ExecuteNonQueryAsync();
            }
        }
        
        private async Task UpdateTeacherDetails(SqlConnection connection, SqlTransaction transaction, string userId)
        {
            string department = Request.Form["department"];
            string specialization = Request.Form["specialization"];
            string address = Request.Form["address"];
            string contactNumber = Request.Form["contactNumber"];
            
            string query = @"
                UPDATE TeacherDetails 
                SET 
                    Department = @Department,
                    Specialization = @Specialization,
                    Address = @Address,
                    ContactNumber = @ContactNumber
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@Department", string.IsNullOrEmpty(department) ? (object)DBNull.Value : department);
                command.Parameters.AddWithValue("@Specialization", string.IsNullOrEmpty(specialization) ? (object)DBNull.Value : specialization);
                command.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(address) ? (object)DBNull.Value : address);
                command.Parameters.AddWithValue("@ContactNumber", string.IsNullOrEmpty(contactNumber) ? (object)DBNull.Value : contactNumber);
                command.Parameters.AddWithValue("@UserId", userId);
                
                await command.ExecuteNonQueryAsync();
            }
        }
        
        private async Task UpdateEmployerDetails(SqlConnection connection, SqlTransaction transaction, string userId)
        {
            string company = Request.Form["company"];
            string industry = Request.Form["industry"];
            string position = Request.Form["position"];
            string phoneNumber = Request.Form["phoneNumber"];
            string address = Request.Form["address"];
            
            string query = @"
                UPDATE EmployerDetails 
                SET 
                    Company = @Company,
                    Industry = @Industry,
                    Position = @Position,
                    PhoneNumber = @PhoneNumber,
                    Address = @Address
                WHERE 
                    UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@Company", string.IsNullOrEmpty(company) ? (object)DBNull.Value : company);
                command.Parameters.AddWithValue("@Industry", string.IsNullOrEmpty(industry) ? (object)DBNull.Value : industry);
                command.Parameters.AddWithValue("@Position", string.IsNullOrEmpty(position) ? (object)DBNull.Value : position);
                command.Parameters.AddWithValue("@PhoneNumber", string.IsNullOrEmpty(phoneNumber) ? (object)DBNull.Value : phoneNumber);
                command.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(address) ? (object)DBNull.Value : address);
                command.Parameters.AddWithValue("@UserId", userId);
                
                await command.ExecuteNonQueryAsync();
            }
        }
        
        private async Task<string> SaveProfilePhoto(IFormFile photo, string role, string userId)
        {
            // Check if valid image
            if (!photo.ContentType.StartsWith("image/"))
            {
                throw new Exception("Only image files are allowed.");
            }
            
            // Create upload directory if it doesn't exist
            string userType = role.ToLower();
            string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", userType + "s");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }
            
            // Generate unique filename
            string fileExtension = Path.GetExtension(photo.FileName);
            string fileName = $"{userType}-{userId}-{DateTime.Now.ToString("yyyyMMddHHmmss")}{fileExtension}";
            string filePath = Path.Combine(uploadsDir, fileName);
            
            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await photo.CopyToAsync(stream);
            }
            
            return $"/uploads/{userType}s/{fileName}";
        }
        
        private async Task UpdateProfilePhoto(SqlConnection connection, SqlTransaction transaction, string role, string userId, string photoUrl)
        {
            string tableName = role + "Details";
            string photoField = role.Equals("employer", StringComparison.OrdinalIgnoreCase) ? "CompanyLogoUrl" : "PhotoUrl";
            
            string query = $@"
                UPDATE {tableName} 
                SET {photoField} = @PhotoUrl
                WHERE UserId = @UserId";
            
            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@PhotoUrl", photoUrl);
                command.Parameters.AddWithValue("@UserId", userId);
                
                await command.ExecuteNonQueryAsync();
            }
        }
        
        [HttpPost]
        [Route("ChangePassword")]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            try
            {
                string userId = HttpContext.Session.GetString("UserId");
                
                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "User not logged in." });
                }
                
                if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
                {
                    return Json(new { success = false, message = "All password fields are required." });
                }
                
                if (newPassword != confirmPassword)
                {
                    return Json(new { success = false, message = "New password and confirmation do not match." });
                }
                
                if (newPassword.Length < 6)
                {
                    return Json(new { success = false, message = "New password must be at least 6 characters long." });
                }
                
                // Validate password meets minimum requirements
                if (newPassword.Length < 8 || !newPassword.Any(char.IsUpper) || !newPassword.Any(c => !char.IsLetterOrDigit(c)))
                {
                    return Json(new { success = false, message = "Password must be at least 8 characters long, contain at least one uppercase letter, and one symbol" });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the stored password hash
                    string getPasswordQuery = "SELECT Password FROM Users WHERE UserId = @UserId";
                    string storedPasswordHash = null;
                    
                    using (var command = new SqlCommand(getPasswordQuery, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            storedPasswordHash = result.ToString();
                        }
                    }
                    
                    if (storedPasswordHash == null)
                    {
                        return Json(new { success = false, message = "User not found." });
                    }
                    
                    // Verify the current password
                    bool isCurrentPasswordValid = PasswordHashService.VerifyPassword(storedPasswordHash, currentPassword);
                    if (!isCurrentPasswordValid)
                    {
                        return Json(new { success = false, message = "Current password is incorrect." });
                    }
                    
                    // Hash the new password
                    string newPasswordHash = PasswordHashService.HashPassword(newPassword);
                    
                    // Update password and reset NeedsPasswordChange flag
                    string updateQuery = "UPDATE Users SET Password = @Password, NeedsPasswordChange = 0 WHERE UserId = @UserId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Password", newPasswordHash);
                        command.Parameters.AddWithValue("@UserId", userId);
                        
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    return Json(new { success = true, message = "Password changed successfully." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to check if a table exists
        private async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }
}