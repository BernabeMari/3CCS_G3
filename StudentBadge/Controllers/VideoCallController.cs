using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace StudentBadge.Controllers
{
    public class VideoCallController : Controller
    {
        private readonly string _connectionString;

        public VideoCallController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Student's video call page
        [HttpGet]
        public IActionResult StudentVideoCall(int callId)
        {
            // Check if student is authenticated
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return RedirectToAction("Login", "Home");
            }

            ViewBag.CallId = callId;
            ViewBag.StudentId = studentId;
            ViewBag.UserType = "student";
            return View();
        }

        // Employer's video call page
        [HttpGet]
        public IActionResult EmployerVideoCall(int callId)
        {
            // Check if employer is authenticated
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return RedirectToAction("Login", "Home");
            }

            ViewBag.CallId = callId;
            ViewBag.EmployerId = employerId;
            ViewBag.UserType = "employer";
            return View();
        }

        // API to get call details
        [HttpGet]
        public async Task<IActionResult> GetCallDetails(int callId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check for current database schema - check if the new tables exist
                    bool usingNewSchema = false;
                    string checkTablesQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME IN ('StudentDetails', 'Users')";
                        
                    using (var checkCommand = new SqlCommand(checkTablesQuery, connection))
                    {
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        usingNewSchema = (count >= 2); // Both tables should exist
                    }
                    
                    // Always try to use the new schema first if tables exist
                    if (usingNewSchema)
                    {
                        // Query for new schema with Users and Details tables
                        string newSchemaQuery = @"
                            SELECT v.CallId, v.EmployerId, v.StudentId, v.Status, v.StartTime, 
                                   u1.FullName AS EmployerName, ed.Company, ed.ProfilePicturePath AS EmployerProfilePic,
                                   u2.FullName AS StudentName, sd.ProfilePicturePath AS StudentProfilePic,
                                   sd.Course, sd.Section, sd.Score, sd.BadgeColor, sd.Achievements, 
                                   sd.IdNumber AS StudentIdNumber
                            FROM VideoCalls v
                            JOIN Users u1 ON v.EmployerId = u1.UserId
                            JOIN EmployerDetails ed ON v.EmployerId = ed.UserId
                            JOIN Users u2 ON v.StudentId = u2.UserId
                            JOIN StudentDetails sd ON v.StudentId = sd.UserId
                            WHERE v.CallId = @CallId";
                        
                        using (var command = new SqlCommand(newSchemaQuery, connection))
                        {
                            command.Parameters.AddWithValue("@CallId", callId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // Extract profile picture from database - it may be stored as a data URL or another format
                                    string studentProfilePic = "/images/blank.jpg";
                                    if (reader["StudentProfilePic"] != DBNull.Value)
                                    {
                                        var profilePicData = reader["StudentProfilePic"].ToString();
                                        
                                        // If already in data URL format, use directly
                                        if (profilePicData.StartsWith("data:"))
                                        {
                                            studentProfilePic = profilePicData;
                                        }
                                        // If it's a raw value but not a path (might be base64 without the prefix)
                                        else if (!profilePicData.StartsWith("/") && !string.IsNullOrEmpty(profilePicData))
                                        {
                                            // Try to convert assuming it's base64 encoded image data
                                            try {
                                                // Add the data URL prefix if missing
                                                studentProfilePic = profilePicData.Contains("data:") 
                                                    ? profilePicData 
                                                    : "data:image/jpeg;base64," + profilePicData;
                                            }
                                            catch {
                                                studentProfilePic = "/images/blank.jpg";
                                            }
                                        }
                                        // Use file path if it looks like one
                                        else if (profilePicData.StartsWith("/"))
                                        {
                                            studentProfilePic = profilePicData;
                                        }
                                    }

                                    var callDetails = new
                                    {
                                        callId = Convert.ToInt32(reader["CallId"]),
                                        employerId = reader["EmployerId"].ToString(),
                                        studentId = reader["StudentId"].ToString(),
                                        status = reader["Status"].ToString(),
                                        startTime = Convert.ToDateTime(reader["StartTime"]),
                                        employerName = reader["EmployerName"].ToString(),
                                        company = reader["Company"].ToString(),
                                        studentName = reader["StudentName"].ToString(),
                                        employerProfilePic = reader["EmployerProfilePic"] != DBNull.Value ? reader["EmployerProfilePic"].ToString() : "/images/blank.jpg",
                                        studentProfilePic = studentProfilePic,
                                        studentIdNumber = reader["StudentIdNumber"].ToString(),
                                        studentCourse = reader["Course"] != DBNull.Value ? reader["Course"].ToString() : "N/A",
                                        studentSection = reader["Section"] != DBNull.Value ? reader["Section"].ToString() : "N/A",
                                        studentScore = reader["Score"] != DBNull.Value ? Convert.ToInt32(reader["Score"]) : 0,
                                        studentBadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "green",
                                        studentAchievements = reader["Achievements"] != DBNull.Value ? reader["Achievements"].ToString() : ""
                                    };
                                    return Json(new { success = true, call = callDetails });
                                }
                            }
                        }
                    }
                    
                    // Fallback to old schema if new schema tables don't exist or didn't find the data
                    string oldSchemaQuery = @"
                        SELECT v.CallId, v.EmployerId, v.StudentId, v.Status, v.StartTime,
                               NULL AS EmployerName, NULL AS Company, NULL AS EmployerProfilePic,
                               NULL AS StudentName, NULL AS StudentProfilePic, 
                               NULL AS Course, NULL AS Section, NULL AS Score, NULL AS BadgeColor, NULL AS Achievements,
                               v.StudentId AS StudentIdNumber
                        FROM VideoCalls v
                        WHERE v.CallId = @CallId";
                        
                    using (var command = new SqlCommand(oldSchemaQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var callDetails = new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    employerId = reader["EmployerId"].ToString(),
                                    studentId = reader["StudentId"].ToString(),
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]),
                                    employerName = "Unknown Employer",
                                    company = "Unknown Company",
                                    studentName = "Unknown Student",
                                    employerProfilePic = "/images/blank.jpg",
                                    studentProfilePic = "/images/blank.jpg",
                                    studentIdNumber = reader["StudentIdNumber"].ToString(),
                                    studentCourse = "N/A",
                                    studentSection = "N/A",
                                    studentScore = 0,
                                    studentBadgeColor = "green",
                                    studentAchievements = ""
                                };
                                return Json(new { success = true, call = callDetails });
                            }
                            
                            return Json(new { success = false, message = "Call not found" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving call details: " + ex.Message });
            }
        }

        // Get calls for a student
        [HttpGet]
        public async Task<IActionResult> GetStudentCalls()
        {
            string studentId = HttpContext.Session.GetString("IdNumber");
            if (string.IsNullOrEmpty(studentId))
            {
                return Json(new { success = false, message = "Student not authenticated" });
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check which database schema we're using
                    bool usingNewSchema = false;
                    string checkTablesQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME IN ('StudentDetails', 'Users')";
                        
                    using (var checkCommand = new SqlCommand(checkTablesQuery, connection))
                    {
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        usingNewSchema = (count >= 2); // Both tables should exist
                    }
                    
                    string query;
                    if (!usingNewSchema)
                    {
                        // Query for old schema with Employers table
                        query = @"
                            SELECT v.CallId, v.EmployerId, v.Status, v.StartTime, 
                                   e.FullName AS EmployerName, e.Company
                            FROM VideoCalls v
                            LEFT JOIN Employers e ON v.EmployerId = e.EmployerId
                            WHERE v.StudentId = @StudentId
                            ORDER BY v.StartTime DESC";
                    }
                    else
                    {
                        // Query for new schema with Users and EmployerDetails tables
                        query = @"
                            SELECT v.CallId, v.EmployerId, v.Status, v.StartTime, 
                                   u.FullName AS EmployerName, ed.Company
                            FROM VideoCalls v
                            JOIN Users u ON v.EmployerId = u.UserId
                            JOIN EmployerDetails ed ON v.EmployerId = ed.UserId
                            WHERE v.StudentId = @StudentId
                            ORDER BY v.StartTime DESC";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var calls = new List<object>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                calls.Add(new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    employerId = reader["EmployerId"].ToString(),
                                    employerName = reader["EmployerName"] != DBNull.Value ? reader["EmployerName"].ToString() : "Unknown Employer",
                                    company = reader["Company"] != DBNull.Value ? reader["Company"].ToString() : "Unknown Company",
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]).ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                        }

                        return Json(new { success = true, calls = calls });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving calls: " + ex.Message });
            }
        }

        // Get calls for an employer
        [HttpGet]
        public async Task<IActionResult> GetEmployerCalls()
        {
            string employerId = HttpContext.Session.GetString("EmployerId");
            if (string.IsNullOrEmpty(employerId))
            {
                return Json(new { success = false, message = "Employer not authenticated" });
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check for current database schema - rather than checking for Students table
                    // we check if StudentDetails and Users tables exist as these are part of the new schema
                    bool usingNewSchema = false;
                    string checkTablesQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME IN ('StudentDetails', 'Users')";
                        
                    using (var checkCommand = new SqlCommand(checkTablesQuery, connection))
                    {
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        usingNewSchema = (count >= 2); // Both tables should exist
                    }
                    
                    string query;
                    if (!usingNewSchema)
                    {
                        // Query for old schema with Students table (if it exists)
                        query = @"
                            SELECT v.CallId, v.StudentId, v.Status, v.StartTime, 
                                   s.FullName AS StudentName, s.Course
                            FROM VideoCalls v
                            LEFT JOIN Students s ON v.StudentId = s.IdNumber
                            WHERE v.EmployerId = @EmployerId
                            ORDER BY v.StartTime DESC";
                    }
                    else
                    {
                        // Query for new schema with Users and StudentDetails tables
                        query = @"
                            SELECT v.CallId, v.StudentId, v.Status, v.StartTime, 
                                   u.FullName AS StudentName, sd.Course
                            FROM VideoCalls v
                            JOIN Users u ON v.StudentId = u.UserId
                            JOIN StudentDetails sd ON v.StudentId = sd.UserId
                            WHERE v.EmployerId = @EmployerId
                            ORDER BY v.StartTime DESC";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        var calls = new List<object>();

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                calls.Add(new
                                {
                                    callId = Convert.ToInt32(reader["CallId"]),
                                    studentId = reader["StudentId"].ToString(),
                                    studentName = reader["StudentName"] != DBNull.Value ? reader["StudentName"].ToString() : "Unknown Student",
                                    course = reader["Course"] != DBNull.Value ? reader["Course"].ToString() : "Unknown Course",
                                    status = reader["Status"].ToString(),
                                    startTime = Convert.ToDateTime(reader["StartTime"]).ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                        }

                        return Json(new { success = true, calls = calls });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error retrieving calls: " + ex.Message });
            }
        }
    }
} 
