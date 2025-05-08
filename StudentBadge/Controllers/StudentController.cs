using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StudentBadge.Models;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class StudentController : Controller
    {
        private readonly ILogger<StudentController> _logger;
        private readonly string _connectionString;

        public StudentController(ILogger<StudentController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GetStudents")]
        public async Task<IActionResult> GetStudents()
        {
            try
            {
                var students = new List<Student>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    string query;
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema (Users + StudentDetails)
                        query = @"
                            SELECT 
                                sd.IdNumber,
                                u.FullName,
                                u.Username,
                                sd.Course,
                                sd.Section,
                                sd.YearLevel,
                                sd.PhotoUrl
                            FROM 
                                StudentDetails sd
                            JOIN 
                                Users u ON sd.UserId = u.UserId
                            ORDER BY 
                                u.FullName";
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema (Students table)
                        query = @"
                            SELECT 
                                IdNumber,
                                FullName,
                                Username,
                                Course,
                                Section,
                                YearLevel,
                                PhotoUrl
                            FROM 
                                Students
                            ORDER BY 
                                FullName";
                    }
                    else
                    {
                        // No valid schema found
                        return Json(new { success = false, message = "No student tables found in database." });
                    }
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var student = new Student
                                {
                                    IdNumber = reader["IdNumber"].ToString(),
                                    FullName = reader["FullName"].ToString(),
                                    Username = reader["Username"].ToString(),
                                    Course = reader["Course"].ToString(),
                                    Section = reader["Section"].ToString()
                                };
                                
                                if (!reader.IsDBNull(reader.GetOrdinal("YearLevel")))
                                {
                                    student.YearLevel = Convert.ToInt32(reader["YearLevel"]);
                                }
                                
                                if (!reader.IsDBNull(reader.GetOrdinal("PhotoUrl")))
                                {
                                    student.PhotoUrl = reader["PhotoUrl"].ToString();
                                }
                                
                                students.Add(student);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, students = students });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving students");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("GetStudent/{id}")]
        public async Task<IActionResult> GetStudent(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    string query;
                    Student student = null;
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema (Users + StudentDetails)
                        query = @"
                            SELECT 
                                sd.IdNumber,
                                u.FullName,
                                u.Username,
                                sd.Course,
                                sd.Section,
                                sd.YearLevel,
                                sd.PhotoUrl
                            FROM 
                                StudentDetails sd
                            JOIN 
                                Users u ON sd.UserId = u.UserId
                            WHERE 
                                sd.IdNumber = @IdNumber";
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema (Students table)
                        query = @"
                            SELECT 
                                IdNumber,
                                FullName,
                                Username,
                                Course,
                                Section,
                                YearLevel,
                                PhotoUrl
                            FROM 
                                Students
                            WHERE 
                                IdNumber = @IdNumber";
                    }
                    else
                    {
                        // No valid schema found
                        return Json(new { success = false, message = "No student tables found in database." });
                    }
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@IdNumber", id);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                student = new Student
                                {
                                    IdNumber = reader["IdNumber"].ToString(),
                                    FullName = reader["FullName"].ToString(),
                                    Username = reader["Username"].ToString(),
                                    Course = reader["Course"].ToString(),
                                    Section = reader["Section"].ToString()
                                };
                                
                                if (!reader.IsDBNull(reader.GetOrdinal("YearLevel")))
                                {
                                    student.YearLevel = Convert.ToInt32(reader["YearLevel"]);
                                }
                                
                                if (!reader.IsDBNull(reader.GetOrdinal("PhotoUrl")))
                                {
                                    student.PhotoUrl = reader["PhotoUrl"].ToString();
                                }
                            }
                        }
                    }
                    
                    if (student == null)
                    {
                        return Json(new { success = false, message = "Student not found." });
                    }
                    
                    return Json(new { success = true, student = student });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving student with ID: {StudentId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateStudent")]
        public async Task<IActionResult> UpdateStudent(string IdNumber, string FullName, string Course, string Section, int YearLevel)
        {
            try
            {
                if (string.IsNullOrEmpty(IdNumber))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema (Users + StudentDetails)
                        
                        // First, get the UserId from StudentDetails
                        string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                        string userId = null;
                        
                        using (var command = new SqlCommand(getUserIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@IdNumber", IdNumber);
                            var result = await command.ExecuteScalarAsync();
                            
                            if (result != null)
                            {
                                userId = result.ToString();
                            }
                            else
                            {
                                return Json(new { success = false, message = "Student not found." });
                            }
                        }
                        
                        // Update the Users table for name
                        string updateUserQuery = "UPDATE Users SET FullName = @FullName WHERE UserId = @UserId";
                        
                        using (var command = new SqlCommand(updateUserQuery, connection))
                        {
                            command.Parameters.AddWithValue("@FullName", FullName);
                            command.Parameters.AddWithValue("@UserId", userId);
                            await command.ExecuteNonQueryAsync();
                        }
                        
                        // Update the StudentDetails table
                        string updateDetailsQuery = @"
                            UPDATE StudentDetails 
                            SET Course = @Course, 
                                Section = @Section, 
                                YearLevel = @YearLevel
                            WHERE IdNumber = @IdNumber";
                        
                        using (var command = new SqlCommand(updateDetailsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Course", Course);
                            command.Parameters.AddWithValue("@Section", Section);
                            command.Parameters.AddWithValue("@YearLevel", YearLevel);
                            command.Parameters.AddWithValue("@IdNumber", IdNumber);
                            
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                return Json(new { success = true, message = "Student updated successfully." });
                            }
                            else
                            {
                                return Json(new { success = false, message = "Student details not found." });
                            }
                        }
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema (Students table)
                        string updateQuery = @"
                            UPDATE Students 
                            SET FullName = @FullName, 
                                Course = @Course, 
                                Section = @Section, 
                                YearLevel = @YearLevel
                            WHERE IdNumber = @IdNumber";
                        
                        using (var command = new SqlCommand(updateQuery, connection))
                        {
                            command.Parameters.AddWithValue("@FullName", FullName);
                            command.Parameters.AddWithValue("@Course", Course);
                            command.Parameters.AddWithValue("@Section", Section);
                            command.Parameters.AddWithValue("@YearLevel", YearLevel);
                            command.Parameters.AddWithValue("@IdNumber", IdNumber);
                            
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                return Json(new { success = true, message = "Student updated successfully." });
                            }
                            else
                            {
                                return Json(new { success = false, message = "Student not found." });
                            }
                        }
                    }
                    else
                    {
                        // No valid schema found
                        return Json(new { success = false, message = "No student tables found in database." });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student with ID: {StudentId}", IdNumber);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("DeleteStudent")]
        public async Task<IActionResult> DeleteStudent(string id)
        {
            try
            {
                _logger.LogInformation($"DeleteStudent called with ID: {id}");
                
                if (string.IsNullOrEmpty(id))
                {
                    _logger.LogWarning("DeleteStudent called with empty ID");
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Database connection opened");
                    
                    // Begin a transaction for all database operations
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // First check which tables exist
                            bool videocallsTableExists = await TableExists(connection, "VideoCalls");
                            bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                            bool studentsTableExists = await TableExists(connection, "Students");
                            bool usersTableExists = await TableExists(connection, "Users");
                            bool attendanceRecordsExists = await TableExists(connection, "AttendanceRecords");
                            bool extraCurricularExists = await TableExists(connection, "ExtraCurricularActivities");
                            bool certificatesTableExists = await TableExists(connection, "Certificates");
                            bool challengeSubmissionsTableExists = await TableExists(connection, "ChallengeSubmissions");
                            
                            _logger.LogInformation($"Table check: VideoCalls={videocallsTableExists}, StudentDetails={studentDetailsExists}, Students={studentsTableExists}, Users={usersTableExists}, AttendanceRecords={attendanceRecordsExists}, ExtraCurricularActivities={extraCurricularExists}, Certificates={certificatesTableExists}, ChallengeSubmissions={challengeSubmissionsTableExists}");
                            
                            // Check if we're using the new or old schema
                            if (studentDetailsExists && usersTableExists)
                            {
                                _logger.LogInformation("Using new table schema (StudentDetails + Users)");
                                
                                // First, get the UserId from StudentDetails
                                string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @IdNumber";
                                string userId = null;
                                
                                using (var command = new SqlCommand(getUserIdQuery, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@IdNumber", id);
                                    var result = await command.ExecuteScalarAsync();
                                    
                                    if (result != null)
                                    {
                                        userId = result.ToString();
                                        _logger.LogInformation($"Found student with UserId: {userId}");
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Student with ID {id} not found in StudentDetails");
                                        transaction.Rollback();
                                        return Json(new { success = false, message = "Student not found." });
                                    }
                                }
                                
                                // Delete certificates first if the table exists
                                if (certificatesTableExists)
                                {
                                    string deleteCertificatesQuery = "DELETE FROM Certificates WHERE StudentId = @StudentId";
                                    using (var certificatesCommand = new SqlCommand(deleteCertificatesQuery, connection, transaction))
                                    {
                                        certificatesCommand.Parameters.AddWithValue("@StudentId", id);
                                        int certificatesDeleted = await certificatesCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {certificatesDeleted} related certificates for student ID {id}");
                                    }
                                }
                                
                                // Delete challenge submissions first if the table exists
                                if (challengeSubmissionsTableExists)
                                {
                                    string deleteChallengeSubmissionsQuery = "DELETE FROM ChallengeSubmissions WHERE StudentId = @StudentId";
                                    using (var challengeSubmissionsCommand = new SqlCommand(deleteChallengeSubmissionsQuery, connection, transaction))
                                    {
                                        challengeSubmissionsCommand.Parameters.AddWithValue("@StudentId", userId); // Note: using userId here, not id
                                        int challengeSubmissionsDeleted = await challengeSubmissionsCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {challengeSubmissionsDeleted} related challenge submissions for UserId {userId}");
                                    }
                                }
                                
                                // Delete from AttendanceRecords if exists
                                if (attendanceRecordsExists)
                                {
                                    string deleteAttendanceQuery = "DELETE FROM AttendanceRecords WHERE StudentId = @StudentId";
                                    using (var attendanceCommand = new SqlCommand(deleteAttendanceQuery, connection, transaction))
                                    {
                                        attendanceCommand.Parameters.AddWithValue("@StudentId", userId);
                                        int attendanceDeleted = await attendanceCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {attendanceDeleted} related attendance records for UserId {userId}");
                                    }
                                }
                                
                                // Delete from ExtraCurricularActivities if exists
                                if (extraCurricularExists)
                                {
                                    string deleteExtraCurricularQuery = "DELETE FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                                    using (var extraCurricularCommand = new SqlCommand(deleteExtraCurricularQuery, connection, transaction))
                                    {
                                        extraCurricularCommand.Parameters.AddWithValue("@StudentId", userId);
                                        int extraCurricularDeleted = await extraCurricularCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {extraCurricularDeleted} related extra-curricular activity records for UserId {userId}");
                                    }
                                }
                                
                                // Delete from VideoCalls first if the table exists (handles FK constraint)
                                if (videocallsTableExists)
                                {
                                    string deleteVideoCallsQuery = "DELETE FROM VideoCalls WHERE StudentId = @StudentId";
                                    using (var videoCallsCommand = new SqlCommand(deleteVideoCallsQuery, connection, transaction))
                                    {
                                        videoCallsCommand.Parameters.AddWithValue("@StudentId", userId);
                                        int videoCallsDeleted = await videoCallsCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {videoCallsDeleted} related video call records for UserId {userId}");
                                    }
                                }
                                
                                // Delete from StudentDetails first (child table)
                                string deleteDetailsQuery = "DELETE FROM StudentDetails WHERE IdNumber = @IdNumber";
                                using (var detailsCommand = new SqlCommand(deleteDetailsQuery, connection, transaction))
                                {
                                    detailsCommand.Parameters.AddWithValue("@IdNumber", id);
                                    int detailsRowsAffected = await detailsCommand.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Deleted {detailsRowsAffected} row(s) from StudentDetails");
                                }
                                
                                // Delete from Users (parent table)
                                string deleteUserQuery = "DELETE FROM Users WHERE UserId = @UserId";
                                using (var userCommand = new SqlCommand(deleteUserQuery, connection, transaction))
                                {
                                    userCommand.Parameters.AddWithValue("@UserId", userId);
                                    int userRowsAffected = await userCommand.ExecuteNonQueryAsync();
                                    _logger.LogInformation($"Deleted {userRowsAffected} row(s) from Users");
                                }
                            }
                            else if (studentsTableExists)
                            {
                                _logger.LogInformation("Using old table schema (Students)");
                                
                                // Check if student exists
                                string checkQuery = "SELECT COUNT(*) FROM Students WHERE IdNumber = @IdNumber";
                                using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                                {
                                    checkCommand.Parameters.AddWithValue("@IdNumber", id);
                                    int studentCount = (int)await checkCommand.ExecuteScalarAsync();
                                    
                                    if (studentCount == 0)
                                    {
                                        _logger.LogWarning($"Student with ID {id} not found in Students table");
                                        transaction.Rollback();
                                        return Json(new { success = false, message = "Student not found." });
                                    }
                                }
                                
                                // Delete certificates first if the table exists
                                if (certificatesTableExists)
                                {
                                    string deleteCertificatesQuery = "DELETE FROM Certificates WHERE StudentId = @StudentId";
                                    using (var certificatesCommand = new SqlCommand(deleteCertificatesQuery, connection, transaction))
                                    {
                                        certificatesCommand.Parameters.AddWithValue("@StudentId", id);
                                        int certificatesDeleted = await certificatesCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {certificatesDeleted} related certificates for student ID {id}");
                                    }
                                }
                                
                                // Delete challenge submissions first if the table exists
                                if (challengeSubmissionsTableExists)
                                {
                                    // First get the UserId for this student
                                    string getUserIdQuery = "SELECT UserId FROM Users WHERE Username = @IdNumber";
                                    string userId = null;
                                    using (var getUserIdCommand = new SqlCommand(getUserIdQuery, connection, transaction))
                                    {
                                        getUserIdCommand.Parameters.AddWithValue("@IdNumber", id);
                                        var result = await getUserIdCommand.ExecuteScalarAsync();
                                        if (result != null)
                                        {
                                            userId = result.ToString();
                                        }
                                    }

                                    if (userId != null)
                                    {
                                        string deleteChallengeSubmissionsQuery = "DELETE FROM ChallengeSubmissions WHERE StudentId = @StudentId";
                                        using (var challengeSubmissionsCommand = new SqlCommand(deleteChallengeSubmissionsQuery, connection, transaction))
                                        {
                                            challengeSubmissionsCommand.Parameters.AddWithValue("@StudentId", userId);
                                            int challengeSubmissionsDeleted = await challengeSubmissionsCommand.ExecuteNonQueryAsync();
                                            _logger.LogInformation($"Deleted {challengeSubmissionsDeleted} related challenge submissions for UserId {userId}");
                                        }
                                    }
                                }
                                
                                // Delete from AttendanceRecords if exists
                                if (attendanceRecordsExists)
                                {
                                    string deleteAttendanceQuery = "DELETE FROM AttendanceRecords WHERE StudentId = @StudentId";
                                    using (var attendanceCommand = new SqlCommand(deleteAttendanceQuery, connection, transaction))
                                    {
                                        attendanceCommand.Parameters.AddWithValue("@StudentId", id);
                                        int attendanceDeleted = await attendanceCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {attendanceDeleted} related attendance records for student ID {id}");
                                    }
                                }
                                
                                // Delete from ExtraCurricularActivities if exists
                                if (extraCurricularExists)
                                {
                                    string deleteExtraCurricularQuery = "DELETE FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                                    using (var extraCurricularCommand = new SqlCommand(deleteExtraCurricularQuery, connection, transaction))
                                    {
                                        extraCurricularCommand.Parameters.AddWithValue("@StudentId", id);
                                        int extraCurricularDeleted = await extraCurricularCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {extraCurricularDeleted} related extra-curricular activity records for student ID {id}");
                                    }
                                }
                                
                                // Delete from VideoCalls first if the table exists (handles FK constraint)
                                if (videocallsTableExists)
                                {
                                    string deleteVideoCallsQuery = "DELETE FROM VideoCalls WHERE StudentId = @StudentId";
                                    using (var videoCallsCommand = new SqlCommand(deleteVideoCallsQuery, connection, transaction))
                                    {
                                        videoCallsCommand.Parameters.AddWithValue("@StudentId", id);
                                        int videoCallsDeleted = await videoCallsCommand.ExecuteNonQueryAsync();
                                        _logger.LogInformation($"Deleted {videoCallsDeleted} related video call records for student ID {id}");
                                    }
                                }
                                
                                // Delete the student
                                string deleteQuery = "DELETE FROM Students WHERE IdNumber = @IdNumber";
                                using (var deleteCommand = new SqlCommand(deleteQuery, connection, transaction))
                                {
                                    deleteCommand.Parameters.AddWithValue("@IdNumber", id);
                                    int rowsAffected = await deleteCommand.ExecuteNonQueryAsync();
                                    
                                    _logger.LogInformation($"Deleted {rowsAffected} row(s) from Students table");
                                }
                            }
                            else
                            {
                                _logger.LogError("No student tables found in database");
                                transaction.Rollback();
                                return Json(new { success = false, message = "No student tables found in database." });
                            }
                            
                            // Commit the transaction
                            transaction.Commit();
                            _logger.LogInformation("Transaction committed successfully");
                            return Json(new { success = true, message = "Student deleted successfully." });
                        }
                        catch (Exception ex)
                        {
                            // Roll back the transaction
                            transaction.Rollback();
                            _logger.LogError($"Error in transaction, rolled back: {ex.Message}");
                            return Json(new { success = false, message = $"Database error: {ex.Message}" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in DeleteStudent: {ex.Message}");
                return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost]
        [Route("UploadStudentPhoto")]
        public async Task<IActionResult> UploadStudentPhoto(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                var file = Request.Form.Files[0];
                
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded." });
                }
                
                // Check file is an image
                if (!file.ContentType.StartsWith("image/"))
                {
                    return Json(new { success = false, message = "Only image files are allowed." });
                }
                
                // Create upload directory if it doesn't exist
                string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "students");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }
                
                // Generate unique filename
                string fileExtension = Path.GetExtension(file.FileName);
                string fileName = $"{studentId}-{DateTime.Now.ToString("yyyyMMddHHmmss")}{fileExtension}";
                string filePath = Path.Combine(uploadsDir, fileName);
                
                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                // Update database with photo URL
                string photoUrl = $"/uploads/students/{fileName}";
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    string updateQuery;
                    
                    if (studentDetailsExists)
                    {
                        // Using new schema
                        updateQuery = "UPDATE StudentDetails SET PhotoUrl = @PhotoUrl WHERE IdNumber = @StudentId";
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema
                        updateQuery = "UPDATE Students SET PhotoUrl = @PhotoUrl WHERE IdNumber = @StudentId";
                    }
                    else
                    {
                        // No valid schema found
                        return Json(new { success = false, message = "No student tables found in database." });
                    }
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@PhotoUrl", photoUrl);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, photoUrl = photoUrl });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to update student photo URL." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading student photo");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("GetStudentDetails/{id}")]
        public async Task<IActionResult> GetStudentDetails(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if we're using the new schema
                    bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                    bool usersTableExists = await TableExists(connection, "Users");
                    bool studentsTableExists = await TableExists(connection, "Students");
                    
                    if (studentDetailsExists && usersTableExists)
                    {
                        // Using new schema
                        string query = @"
                            SELECT 
                                sd.IdNumber,
                                u.FullName,
                                u.Username,
                                u.Email,
                                sd.Course,
                                sd.Section,
                                sd.YearLevel,
                                sd.PhotoUrl,
                                sd.Address,
                                sd.ContactNumber,
                                sd.GuardianName,
                                sd.GuardianContact
                            FROM 
                                StudentDetails sd
                            JOIN 
                                Users u ON sd.UserId = u.UserId
                            WHERE 
                                sd.IdNumber = @IdNumber";
                        
                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@IdNumber", id);
                            
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    var student = new
                                    {
                                        IdNumber = reader["IdNumber"].ToString(),
                                        FullName = reader["FullName"].ToString(),
                                        Username = reader["Username"].ToString(),
                                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString(),
                                        Course = reader["Course"].ToString(),
                                        Section = reader["Section"].ToString(),
                                        YearLevel = reader.IsDBNull(reader.GetOrdinal("YearLevel")) ? (int?)null : Convert.ToInt32(reader["YearLevel"]),
                                        PhotoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader["PhotoUrl"].ToString(),
                                        Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString(),
                                        ContactNumber = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader["ContactNumber"].ToString(),
                                        GuardianName = reader.IsDBNull(reader.GetOrdinal("GuardianName")) ? null : reader["GuardianName"].ToString(),
                                        GuardianContact = reader.IsDBNull(reader.GetOrdinal("GuardianContact")) ? null : reader["GuardianContact"].ToString()
                                    };
                                    
                                    return Json(new { success = true, student = student });
                                }
                            }
                        }
                    }
                    else if (studentsTableExists)
                    {
                        // Using old schema
                        string query = @"
                            SELECT 
                                IdNumber,
                                FullName,
                                Username,
                                Email,
                                Course,
                                Section,
                                YearLevel,
                                PhotoUrl,
                                Address,
                                ContactNumber,
                                GuardianName,
                                GuardianContact
                            FROM 
                                Students
                            WHERE 
                                IdNumber = @IdNumber";
                        
                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@IdNumber", id);
                            
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    var student = new
                                    {
                                        IdNumber = reader["IdNumber"].ToString(),
                                        FullName = reader["FullName"].ToString(),
                                        Username = reader["Username"].ToString(),
                                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString(),
                                        Course = reader["Course"].ToString(),
                                        Section = reader["Section"].ToString(),
                                        YearLevel = reader.IsDBNull(reader.GetOrdinal("YearLevel")) ? (int?)null : Convert.ToInt32(reader["YearLevel"]),
                                        PhotoUrl = reader.IsDBNull(reader.GetOrdinal("PhotoUrl")) ? null : reader["PhotoUrl"].ToString(),
                                        Address = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString(),
                                        ContactNumber = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader["ContactNumber"].ToString(),
                                        GuardianName = reader.IsDBNull(reader.GetOrdinal("GuardianName")) ? null : reader["GuardianName"].ToString(),
                                        GuardianContact = reader.IsDBNull(reader.GetOrdinal("GuardianContact")) ? null : reader["GuardianContact"].ToString()
                                    };
                                    
                                    return Json(new { success = true, student = student });
                                }
                            }
                        }
                    }
                    
                    return Json(new { success = false, message = "Student not found." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving student details with ID: {StudentId}", id);
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