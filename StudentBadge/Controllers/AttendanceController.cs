using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Dynamic;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Net.Http;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class AttendanceController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AttendanceController> _logger;
        private readonly IWebHostEnvironment _hostingEnvironment;

        public AttendanceController(IConfiguration configuration, ILogger<AttendanceController> logger, IWebHostEnvironment hostingEnvironment)
        {
            _configuration = configuration;
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost]
        [Route("RecordAttendance")]
        public async Task<IActionResult> RecordAttendance(string studentId, string teacherId, string eventName, string eventDate, string eventDescription, IFormFile proofImage, decimal? score = 100)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(eventDate))
                {
                    return Json(new { success = false, message = "Student ID, Event Name, and Event Date are required." });
                }

                // IMPORTANT: Always use 100 as the score for webinar attendance
                // This ensures consistency across all attendance records
                score = 100;
                
                _logger.LogInformation($"Recording attendance for student {studentId}, event {eventName} with score set to {score}");

                DateTime attendanceDate;
                if (!DateTime.TryParseExact(eventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out attendanceDate))
                {
                    return Json(new { success = false, message = "Invalid date format." });
                }

                // Process and save the proof image
                string proofImagePath = null;
                if (proofImage != null && proofImage.Length > 0)
                {
                    var uploadsFolder = Path.Combine(_hostingEnvironment.WebRootPath, "uploads", "attendance");
                    Directory.CreateDirectory(uploadsFolder); // Create directory if it doesn't exist
                    
                    var uniqueFileName = $"{studentId}_{DateTime.Now.Ticks}_{Path.GetFileName(proofImage.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await proofImage.CopyToAsync(fileStream);
                    }
                    
                    proofImagePath = $"/uploads/attendance/{uniqueFileName}";
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First ensure the table exists with the correct schema
                    await EnsureAttendanceRecordsTableExists(connection);
                    
                    // Check if Score column exists
                    bool scoreColumnExists = await ColumnExists(connection, "AttendanceRecords", "Score");
                    
                    if (!scoreColumnExists)
                    {
                        _logger.LogWarning("Score column does not exist. Creating it...");
                        using (var command = new SqlCommand("ALTER TABLE AttendanceRecords ADD Score decimal(18,2) DEFAULT 100", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created Score column with default value 100");
                        }
                    }
                    
                    // Try to resolve the actual UserId if studentId is an ID number
                    string actualStudentId = studentId;
                    string checkUserIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @StudentId";
                    using (var command = new SqlCommand(checkUserIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int count = (int)await command.ExecuteScalarAsync();
                        if (count == 0)
                        {
                            // studentId is not a UserId, try to get the actual UserId
                            string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var idCommand = new SqlCommand(getUserIdQuery, connection))
                            {
                                idCommand.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await idCommand.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    actualStudentId = result.ToString();
                                    _logger.LogInformation($"Resolved StudentId {studentId} to UserId {actualStudentId}");
                                }
                            }
                        }
                    }
                    
                    // Always use the query with the Score column
                    string insertQuery = @"
                        INSERT INTO AttendanceRecords (StudentId, TeacherId, EventName, EventDate, EventDescription, ProofImagePath, CreatedAt, Score) 
                        VALUES (@StudentId, @TeacherId, @EventName, @EventDate, @EventDescription, @ProofImagePath, @CreatedAt, @Score);
                        SELECT SCOPE_IDENTITY();";
                    
                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", actualStudentId);
                        command.Parameters.AddWithValue("@TeacherId", teacherId ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@EventName", eventName);
                        command.Parameters.AddWithValue("@EventDate", attendanceDate.Date);
                        command.Parameters.AddWithValue("@EventDescription", eventDescription ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProofImagePath", proofImagePath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                        command.Parameters.AddWithValue("@Score", score.Value);
                        
                        var newId = await command.ExecuteScalarAsync();
                        int attendanceId = Convert.ToInt32(newId);
                        
                        // Verify the score was saved correctly
                        using (var verifyCommand = new SqlCommand("SELECT Score FROM AttendanceRecords WHERE AttendanceId = @AttendanceId", connection))
                        {
                            verifyCommand.Parameters.AddWithValue("@AttendanceId", attendanceId);
                            var savedScore = await verifyCommand.ExecuteScalarAsync();
                            _logger.LogInformation($"Verified score in database for attendance {attendanceId}: {savedScore}");
                        }
                        
                        // After recording attendance, recalculate the seminar score
                        try
                        {
                            // First try to update directly
                            await UpdateSeminarScoreDirectly(connection, actualStudentId);
                            
                            // Then ensure the score is properly calculated by making an HTTP request to the Score controller
                            using (var httpClient = new HttpClient())
                            {
                                httpClient.BaseAddress = new Uri($"{Request.Scheme}://{Request.Host}");
                                var response = await httpClient.PostAsync($"/Score/CalculateSeminarsScoreFromAttendance?studentId={actualStudentId}", null);
                                
                                if (response.IsSuccessStatusCode)
                                {
                                    _logger.LogInformation($"Successfully updated seminar score via Score controller for student {actualStudentId}");
                                }
                                else
                                {
                                    _logger.LogWarning($"HTTP request to update seminar score returned status code {response.StatusCode}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error recalculating seminar score for student {actualStudentId} after recording attendance");
                            // Fallback to direct update when recalculation fails
                            await UpdateSeminarScoreDirectly(connection, actualStudentId);
                        }
                        
                        return Json(new { 
                            success = true, 
                            message = "Attendance recorded successfully with score " + score.Value, 
                            recordId = attendanceId 
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording attendance for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetAttendance/{studentId}")]
        public async Task<IActionResult> GetAttendance(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                List<object> attendanceRecords = new List<object>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "AttendanceRecords"))
                    {
                        return Json(new { success = true, attendanceRecords = attendanceRecords });
                    }
                    
                    string query = @"
                        SELECT AttendanceId, AttendanceDate, Status, Notes 
                        FROM AttendanceRecords 
                        WHERE StudentId = @StudentId 
                        ORDER BY AttendanceDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                attendanceRecords.Add(new
                                {
                                    id = Convert.ToInt32(reader["AttendanceId"]),
                                    date = Convert.ToDateTime(reader["AttendanceDate"]).ToString("yyyy-MM-dd"),
                                    status = reader["Status"].ToString(),
                                    notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader["Notes"].ToString()
                                });
                            }
                        }
                    }
                }
                
                return Json(new { success = true, attendanceRecords = attendanceRecords });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpDelete]
        [Route("DeleteAttendanceRecord/{id}")]
        public async Task<IActionResult> DeleteAttendanceRecord(int id)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First get the studentId for this attendance record
                    string getStudentIdQuery = "SELECT StudentId FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";
                    string studentId = null;
                    
                    using (var command = new SqlCommand(getStudentIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceId", id);
                        var result = await command.ExecuteScalarAsync();
                        studentId = result?.ToString();
                    }
                    
                    if (string.IsNullOrEmpty(studentId))
                    {
                        return Json(new { success = false, message = "Attendance record not found." });
                    }
                    
                    // Delete the attendance record
                    string deleteQuery = "DELETE FROM AttendanceRecords WHERE AttendanceId = @AttendanceId";
                    
                    using (var command = new SqlCommand(deleteQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceId", id);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            // Now recalculate the seminar score for this student
                            try
                            {
                                await UpdateSeminarScoreDirectly(connection, studentId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error recalculating seminar score for student {studentId} after attendance deletion");
                            }
                            
                            return Json(new { 
                                success = true, 
                                message = "Attendance record deleted successfully.",
                                studentId = studentId 
                            });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Attendance record not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting attendance record with ID: {AttendanceId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetAttendanceByDate")]
        public async Task<IActionResult> GetAttendanceByDate(string date)
        {
            try
            {
                if (string.IsNullOrEmpty(date))
                {
                    return Json(new { success = false, message = "Date is required." });
                }

                DateTime attendanceDate;
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out attendanceDate))
                {
                    return Json(new { success = false, message = "Invalid date format." });
                }
                
                List<dynamic> attendanceRecords = new List<dynamic>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "AttendanceRecords"))
                    {
                        return Json(new { success = true, attendanceRecords = attendanceRecords });
                    }
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    string query;
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema
                        query = @"
                            SELECT 
                                a.AttendanceId, 
                                a.StudentId, 
                                u.FullName AS StudentName, 
                                sd.Course, 
                                sd.Section, 
                                a.Status, 
                                a.Notes 
                            FROM 
                                AttendanceRecords a
                            JOIN 
                                StudentDetails sd ON a.StudentId = sd.UserId
                            JOIN 
                                Users u ON sd.UserId = u.UserId
                            WHERE 
                                a.AttendanceDate = @AttendanceDate
                            ORDER BY 
                                u.FullName";
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema
                        query = @"
                            SELECT 
                                a.AttendanceId, 
                                a.StudentId, 
                                s.FullName AS StudentName, 
                                s.Course, 
                                s.Section, 
                                a.Status, 
                                a.Notes 
                            FROM 
                                AttendanceRecords a
                            JOIN 
                                Students s ON a.StudentId = s.IdNumber
                            WHERE 
                                a.AttendanceDate = @AttendanceDate
                            ORDER BY 
                                s.FullName";
                    }
                    else
                    {
                        // Fallback simple query with no joins
                        query = @"
                            SELECT 
                                AttendanceId, 
                                StudentId, 
                                Status, 
                                Notes 
                            FROM 
                                AttendanceRecords
                            WHERE 
                                AttendanceDate = @AttendanceDate";
                    }
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@AttendanceDate", attendanceDate.Date);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                dynamic record = new ExpandoObject();
                                record.id = Convert.ToInt32(reader["AttendanceId"]);
                                record.studentId = reader["StudentId"].ToString();
                                record.status = reader["Status"].ToString();
                                record.notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader["Notes"].ToString();
                                
                                // Add additional properties if they exist in the result
                                if (HasColumn(reader, "StudentName") && !reader.IsDBNull(reader.GetOrdinal("StudentName")))
                                {
                                    record.studentName = reader["StudentName"].ToString();
                                }
                                
                                if (HasColumn(reader, "Course") && !reader.IsDBNull(reader.GetOrdinal("Course")))
                                {
                                    record.course = reader["Course"].ToString();
                                }
                                
                                if (HasColumn(reader, "Section") && !reader.IsDBNull(reader.GetOrdinal("Section")))
                                {
                                    record.section = reader["Section"].ToString();
                                }
                                
                                attendanceRecords.Add(record);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, attendanceRecords = attendanceRecords });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance records for date {Date}", date);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetAttendanceRecords")]
        public async Task<IActionResult> GetAttendanceRecords(string startDate, string endDate)
        {
            try
            {
                if (string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate))
                {
                    return Json(new { success = false, message = "Start date and end date are required." });
                }

                DateTime start, end;
                if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                {
                    return Json(new { success = false, message = "Invalid start date format." });
                }
                
                if (!DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                {
                    return Json(new { success = false, message = "Invalid end date format." });
                }
                
                List<dynamic> attendanceRecords = new List<dynamic>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "AttendanceRecords"))
                    {
                        return Json(new { success = true, attendanceRecords = attendanceRecords });
                    }
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    string query;
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema
                        query = @"
                            SELECT 
                                a.AttendanceId, 
                                a.StudentId, 
                                u.FullName AS StudentName, 
                                sd.Course, 
                                sd.Section, 
                                a.AttendanceDate,
                                a.Status, 
                                a.Notes 
                            FROM 
                                AttendanceRecords a
                            JOIN 
                                StudentDetails sd ON a.StudentId = sd.UserId
                            JOIN 
                                Users u ON sd.UserId = u.UserId
                            WHERE 
                                a.AttendanceDate BETWEEN @StartDate AND @EndDate
                            ORDER BY 
                                a.AttendanceDate DESC, u.FullName";
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema
                        query = @"
                            SELECT 
                                a.AttendanceId, 
                                a.StudentId, 
                                s.FullName AS StudentName, 
                                s.Course, 
                                s.Section, 
                                a.AttendanceDate,
                                a.Status, 
                                a.Notes 
                            FROM 
                                AttendanceRecords a
                            JOIN 
                                Students s ON a.StudentId = s.IdNumber
                            WHERE 
                                a.AttendanceDate BETWEEN @StartDate AND @EndDate
                            ORDER BY 
                                a.AttendanceDate DESC, s.FullName";
                    }
                    else
                    {
                        // Fallback simple query with no joins
                        query = @"
                            SELECT 
                                AttendanceId, 
                                StudentId, 
                                AttendanceDate,
                                Status, 
                                Notes 
                            FROM 
                                AttendanceRecords
                            WHERE 
                                AttendanceDate BETWEEN @StartDate AND @EndDate
                            ORDER BY 
                                AttendanceDate DESC";
                    }
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StartDate", start.Date);
                        command.Parameters.AddWithValue("@EndDate", end.Date);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                dynamic record = new ExpandoObject();
                                record.id = Convert.ToInt32(reader["AttendanceId"]);
                                record.studentId = reader["StudentId"].ToString();
                                record.date = Convert.ToDateTime(reader["AttendanceDate"]).ToString("yyyy-MM-dd");
                                record.status = reader["Status"].ToString();
                                record.notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader["Notes"].ToString();
                                
                                // Add additional properties if they exist in the result
                                if (HasColumn(reader, "StudentName") && !reader.IsDBNull(reader.GetOrdinal("StudentName")))
                                {
                                    record.studentName = reader["StudentName"].ToString();
                                }
                                
                                if (HasColumn(reader, "Course") && !reader.IsDBNull(reader.GetOrdinal("Course")))
                                {
                                    record.course = reader["Course"].ToString();
                                }
                                
                                if (HasColumn(reader, "Section") && !reader.IsDBNull(reader.GetOrdinal("Section")))
                                {
                                    record.section = reader["Section"].ToString();
                                }
                                
                                attendanceRecords.Add(record);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, attendanceRecords = attendanceRecords });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attendance records for date range {StartDate} to {EndDate}", startDate, endDate);
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task UpdateSeminarScoreDirectly(SqlConnection connection, string studentId)
        {
            try
            {
                _logger.LogInformation($"Directly updating seminar score for student {studentId}");
                
                // Ensure the StudentDetails table has a SeminarsWebinarsScore column
                if (!await ColumnExists(connection, "StudentDetails", "SeminarsWebinarsScore"))
                {
                    _logger.LogWarning("SeminarsWebinarsScore column does not exist in StudentDetails table. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD SeminarsWebinarsScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created SeminarsWebinarsScore column with default value 0");
                    }
                }

                // Calculate seminar score based on attendance records
                decimal seminarScore = 0;
                
                // Get the count of verified attendance records
                string countQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                int totalAttendanceCount = 0;
                
                using (var command = new SqlCommand(countQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    totalAttendanceCount = (int)await command.ExecuteScalarAsync();
                    _logger.LogInformation($"Found {totalAttendanceCount} attendance records for student {studentId}");
                }
                
                // Check if Score column exists in AttendanceRecords
                if (await ColumnExists(connection, "AttendanceRecords", "Score"))
                {
                    // Sum attendance scores if Score column exists
                    string sumQuery = "SELECT COALESCE(SUM(Score), 0) FROM AttendanceRecords WHERE StudentId = @StudentId";
                    decimal totalScore = 0;
                    
                    using (var command = new SqlCommand(sumQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            totalScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Total attendance score for student {studentId}: {totalScore}");
                        }
                    }
                    
                    // Calculate: 1 point per 100 score points, max 10 points
                    seminarScore = Math.Min(Math.Floor(totalScore / 100), 10);
                }
                else
                {
                    // Calculate: 1 point per attendance, max 10 points
                    seminarScore = Math.Min(totalAttendanceCount, 10);
                }
                
                _logger.LogInformation($"Calculated seminar score for student {studentId}: {seminarScore} based on {totalAttendanceCount} attendance records");
                
                // Update the StudentDetails table with both UserId and IdNumber attempts
                bool updated = false;
                
                // First try with UserId
                string updateQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE UserId = @StudentId";
                int rowsAffected = 0;
                
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated SeminarsWebinarsScore to {seminarScore} for student with UserId {studentId}");
                    }
                }
                
                // If not updated, try with IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated SeminarsWebinarsScore to {seminarScore} for student with IdNumber {studentId}");
                        }
                    }
                }
                
                if (!updated)
                {
                    _logger.LogWarning($"Failed to update SeminarsWebinarsScore for student {studentId}. No matching record found.");
                }
                
                // Now update the overall score
                await RecalculateOverallScore(connection, studentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error directly updating seminar score for student {StudentId}", studentId);
            }
        }
        
        private async Task RecalculateOverallScore(SqlConnection connection, string studentId)
        {
            try
            {
                // Get score components from StudentDetails
                string query = @"
                    SELECT 
                        COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                        COALESCE(ExtracurricularScore, 0) as ExtracurricularScore, 
                        COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                        COALESCE(CertificationsScore, 0) as CertificationsScore,
                        COALESCE(ChallengesCompletedScore, 0) as ChallengesCompletedScore
                    FROM StudentDetails 
                    WHERE UserId = @StudentId";
                
                decimal academicScore = 0;
                decimal extracurricularScore = 0;
                decimal seminarScore = 0;
                decimal certificationScore = 0;
                decimal challengesScore = 0;
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            if (!reader.IsDBNull(reader.GetOrdinal("AcademicGradesScore")))
                                academicScore = reader.GetDecimal(reader.GetOrdinal("AcademicGradesScore"));
                            
                            if (!reader.IsDBNull(reader.GetOrdinal("ExtracurricularScore")))
                                extracurricularScore = reader.GetDecimal(reader.GetOrdinal("ExtracurricularScore"));
                            
                            if (!reader.IsDBNull(reader.GetOrdinal("SeminarsWebinarsScore")))
                                seminarScore = reader.GetDecimal(reader.GetOrdinal("SeminarsWebinarsScore"));
                            
                            if (!reader.IsDBNull(reader.GetOrdinal("CertificationsScore")))
                                certificationScore = reader.GetDecimal(reader.GetOrdinal("CertificationsScore"));
                            
                            if (!reader.IsDBNull(reader.GetOrdinal("ChallengesCompletedScore")))
                                challengesScore = reader.GetDecimal(reader.GetOrdinal("ChallengesCompletedScore"));
                        }
                    }
                }
                
                // Calculate overall score with correct weightings
                decimal overallScore = 
                    (academicScore * 0.3m) +
                    (extracurricularScore * 0.2m) +
                    (seminarScore * 0.1m) +
                    (certificationScore * 0.2m) +
                    (challengesScore * 0.2m);
                
                // Update the overall score
                string updateQuery = "UPDATE StudentDetails SET Score = @Score WHERE UserId = @StudentId";
                int rowsAffected = 0;
                
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Score", overallScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    rowsAffected = await command.ExecuteNonQueryAsync();
                }
                
                // If no rows affected, try using IdNumber
                if (rowsAffected == 0)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET Score = @Score WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", overallScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        rowsAffected = await command.ExecuteNonQueryAsync();
                    }
                }
                
                _logger.LogInformation($"Recalculated overall score for student {studentId}: {overallScore}, affected rows: {rowsAffected}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating overall score for student {StudentId}", studentId);
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
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }

        // Helper method to check if a column exists in a table
        private async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }
        
        // Helper method to check if a reader has a column
        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        // Helper method to ensure the AttendanceRecords table exists with the right schema
        private async Task EnsureAttendanceRecordsTableExists(SqlConnection connection)
        {
            string checkTableQuery = @"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AttendanceRecords')
                BEGIN
                    CREATE TABLE AttendanceRecords (
                        AttendanceId INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(50) NOT NULL,
                        TeacherId NVARCHAR(50) NULL,
                        EventName NVARCHAR(255) NOT NULL,
                        EventDate DATE NOT NULL,
                        EventDescription NVARCHAR(MAX) NULL,
                        ProofImagePath NVARCHAR(500) NULL,
                        Score decimal(18,2) DEFAULT 100,
                        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
                    )
                END";
            
            using (var command = new SqlCommand(checkTableQuery, connection))
            {
                await command.ExecuteNonQueryAsync();
            }
            
            // Check if the table exists but the Score column has the wrong data type
            string checkScoreColumnTypeQuery = @"
                IF EXISTS (
                    SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = 'AttendanceRecords' 
                    AND COLUMN_NAME = 'Score' 
                    AND DATA_TYPE = 'int'
                )
                BEGIN
                    -- Create temporary column
                    ALTER TABLE AttendanceRecords ADD ScoreNew decimal(18,2) DEFAULT 100;
                    
                    -- Copy data, converting from int to decimal
                    UPDATE AttendanceRecords SET ScoreNew = CAST(Score AS decimal(18,2));
                    
                    -- Drop old column
                    ALTER TABLE AttendanceRecords DROP COLUMN Score;
                    
                    -- Rename new column
                    EXEC sp_rename 'AttendanceRecords.ScoreNew', 'Score', 'COLUMN';
                END";
                
            try
            {
                using (var command = new SqlCommand(checkScoreColumnTypeQuery, connection))
                {
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Checked and fixed Score column type if needed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/fixing Score column type: {Message}", ex.Message);
                // Continue regardless of error - the application can still function
            }
        }
    }
}