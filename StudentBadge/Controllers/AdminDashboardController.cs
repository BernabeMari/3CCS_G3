using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class AdminDashboardController : Controller
    {
        private readonly ILogger<AdminDashboardController> _logger;
        private readonly string _connectionString;

        public AdminDashboardController(ILogger<AdminDashboardController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("")]
        public async Task<IActionResult> AdminDashboard()
        {
            try
            {
                // Check if user is admin
                if (HttpContext.Session.GetString("Role") != "admin")
                {
                    return RedirectToAction("Login", "Account");
                }

                // Set the admin name from session
                ViewBag.AdminName = HttpContext.Session.GetString("FullName") ?? "Admin";

                var dashboardData = await GetDashboardStats();
                return View(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                ViewBag.Error = "Error loading admin dashboard: " + ex.Message;
                return View();
            }
        }

        [HttpGet]
        [Route("GetDashboardStats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            try
            {
                dynamic stats = new ExpandoObject();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get total students count
                    stats.totalStudents = await GetTotalStudents(connection);
                    
                    // Get total teachers count
                    stats.totalTeachers = await GetTotalTeachers(connection);
                    
                    // Get total courses count
                    stats.totalCourses = await GetDistinctCourseCount(connection);
                    
                    // Get total sections count
                    stats.totalSections = await GetDistinctSectionCount(connection);
                    
                    // Get recent activities/test submissions
                    stats.recentActivities = await GetRecentActivities(connection);
                    
                    // Get attendance summary
                    stats.attendanceSummary = await GetAttendanceSummary(connection);
                    
                    // Get student performance statistics
                    stats.performanceStats = await GetPerformanceStats(connection);
                }
                
                return Json(new { success = true, stats = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard statistics");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<int> GetTotalStudents(SqlConnection connection)
        {
            // Check which tables exist
            bool studentDetailsExists = await TableExists(connection, "StudentDetails");
            bool studentsTableExists = await TableExists(connection, "Students");
            
            string query;
            
            if (studentDetailsExists)
            {
                query = "SELECT COUNT(*) FROM StudentDetails";
            }
            else if (studentsTableExists)
            {
                query = "SELECT COUNT(*) FROM Students";
            }
            else
            {
                return 0;
            }
            
            using (var command = new SqlCommand(query, connection))
            {
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
        }
        
        private async Task<int> GetTotalTeachers(SqlConnection connection)
        {
            // Check which tables exist
            bool teacherDetailsExists = await TableExists(connection, "TeacherDetails");
            bool teachersTableExists = await TableExists(connection, "Teachers");
            bool usersTableExists = await TableExists(connection, "Users");
            
            string query;
            
            if (teacherDetailsExists)
            {
                query = "SELECT COUNT(*) FROM TeacherDetails";
            }
            else if (teachersTableExists)
            {
                query = "SELECT COUNT(*) FROM Teachers";
            }
            else if (usersTableExists)
            {
                query = "SELECT COUNT(*) FROM Users WHERE Role = 'teacher'";
            }
            else
            {
                return 0;
            }
            
            using (var command = new SqlCommand(query, connection))
            {
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }
        
        private async Task<int> GetDistinctCourseCount(SqlConnection connection)
        {
            // Check which tables exist
            bool studentDetailsExists = await TableExists(connection, "StudentDetails");
            bool studentsTableExists = await TableExists(connection, "Students");
            
            string query;
            
            if (studentDetailsExists)
            {
                query = "SELECT COUNT(DISTINCT Course) FROM StudentDetails WHERE Course IS NOT NULL";
            }
            else if (studentsTableExists)
            {
                query = "SELECT COUNT(DISTINCT Course) FROM Students WHERE Course IS NOT NULL";
            }
            else
            {
                return 0;
            }
            
            using (var command = new SqlCommand(query, connection))
            {
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }
        
        private async Task<int> GetDistinctSectionCount(SqlConnection connection)
        {
            // Check which tables exist
            bool studentDetailsExists = await TableExists(connection, "StudentDetails");
            bool studentsTableExists = await TableExists(connection, "Students");
            
            string query;
            
            if (studentDetailsExists)
            {
                query = "SELECT COUNT(DISTINCT Section) FROM StudentDetails WHERE Section IS NOT NULL";
            }
            else if (studentsTableExists)
            {
                query = "SELECT COUNT(DISTINCT Section) FROM Students WHERE Section IS NOT NULL";
            }
            else
            {
                return 0;
            }
            
            using (var command = new SqlCommand(query, connection))
            {
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }
        
        private async Task<List<dynamic>> GetRecentActivities(SqlConnection connection)
        {
            List<dynamic> activities = new List<dynamic>();
            
            // Check if relevant tables exist
            bool testSubmissionsExists = await TableExists(connection, "ProgrammingTestSubmissions");
            bool certificatesExists = await TableExists(connection, "Certificates");
            bool challengeSubmissionsExists = await TableExists(connection, "ChallengeSubmissions");
            
            if (testSubmissionsExists)
            {
                await AddRecentTestSubmissions(connection, activities);
            }
            
            if (certificatesExists)
            {
                await AddRecentCertificates(connection, activities);
            }
            
            if (challengeSubmissionsExists)
            {
                await AddRecentChallengeSubmissions(connection, activities);
            }
            
            // Sort by most recent date
            activities.Sort((a, b) => DateTime.Compare(b.date, a.date));
            
            // Return only the most recent 10 activities
            return activities.Count > 10 ? activities.GetRange(0, 10) : activities;
        }
        
        private async Task AddRecentTestSubmissions(SqlConnection connection, List<dynamic> activities)
        {
            string query = @"
                SELECT TOP 10
                    ts.SubmissionId,
                    ts.StudentId,
                    s.FullName AS StudentName,
                    ts.TestId,
                    ts.TestName,
                    ts.SubmissionDate,
                    ts.Score
                FROM 
                    ProgrammingTestSubmissions ts
                JOIN 
                    Students s ON ts.StudentId = s.IdNumber
                ORDER BY 
                    ts.SubmissionDate DESC";
                    
            using (var command = new SqlCommand(query, connection))
            {
                try
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            dynamic activity = new ExpandoObject();
                            activity.type = "TestSubmission";
                            activity.id = Convert.ToInt32(reader["SubmissionId"]);
                            activity.studentId = reader["StudentId"].ToString();
                            activity.studentName = reader["StudentName"].ToString();
                            activity.testId = Convert.ToInt32(reader["TestId"]);
                            activity.testName = reader["TestName"].ToString();
                            activity.date = Convert.ToDateTime(reader["SubmissionDate"]);
                            activity.score = Convert.ToInt32(reader["Score"]);
                            activity.description = $"{activity.studentName} completed test {activity.testName} with score {activity.score}";
                            
                            activities.Add(activity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving recent test submissions");
                }
            }
        }
        
        private async Task AddRecentCertificates(SqlConnection connection, List<dynamic> activities)
        {
            string query = @"
                SELECT TOP 10
                    CertificateId,
                    StudentId,
                    StudentName,
                    TestId,
                    TestName,
                    IssueDate,
                    Score
                FROM 
                    Certificates
                ORDER BY 
                    IssueDate DESC";
                    
            using (var command = new SqlCommand(query, connection))
            {
                try
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            dynamic activity = new ExpandoObject();
                            activity.type = "Certificate";
                            activity.id = Convert.ToInt32(reader["CertificateId"]);
                            activity.studentId = reader["StudentId"].ToString();
                            activity.studentName = reader["StudentName"].ToString();
                            activity.testId = Convert.ToInt32(reader["TestId"]);
                            activity.testName = reader["TestName"].ToString();
                            activity.date = Convert.ToDateTime(reader["IssueDate"]);
                            activity.score = Convert.ToInt32(reader["Score"]);
                            activity.description = $"{activity.studentName} earned a certificate for {activity.testName}";
                            
                            activities.Add(activity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving recent certificates");
                }
            }
        }
        
        private async Task AddRecentChallengeSubmissions(SqlConnection connection, List<dynamic> activities)
        {
            string query = @"
                SELECT TOP 10
                    cs.SubmissionId,
                    cs.StudentId,
                    s.FullName AS StudentName,
                    cs.ChallengeId,
                    c.ChallengeName,
                    cs.SubmissionDate,
                    cs.PercentageScore
                FROM 
                    ChallengeSubmissions cs
                JOIN 
                    Students s ON cs.StudentId = s.IdNumber
                JOIN 
                    Challenges c ON cs.ChallengeId = c.ChallengeId
                ORDER BY 
                    cs.SubmissionDate DESC";
                    
            using (var command = new SqlCommand(query, connection))
            {
                try
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            dynamic activity = new ExpandoObject();
                            activity.type = "ChallengeSubmission";
                            activity.id = Convert.ToInt32(reader["SubmissionId"]);
                            activity.studentId = reader["StudentId"].ToString();
                            activity.studentName = reader["StudentName"].ToString();
                            activity.challengeId = Convert.ToInt32(reader["ChallengeId"]);
                            activity.challengeName = reader["ChallengeName"].ToString();
                            activity.date = Convert.ToDateTime(reader["SubmissionDate"]);
                            activity.score = Convert.ToInt32(reader["PercentageScore"]);
                            activity.description = $"{activity.studentName} completed challenge {activity.challengeName} with score {activity.score}%";
                            
                            activities.Add(activity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving recent challenge submissions");
                }
            }
        }
        
        private async Task<dynamic> GetAttendanceSummary(SqlConnection connection)
        {
            dynamic summary = new ExpandoObject();
            summary.present = 0;
            summary.absent = 0;
            summary.excused = 0;
            summary.late = 0;
            
            // Check if attendance table exists
            if (!await TableExists(connection, "AttendanceRecords"))
            {
                return summary;
            }
            
            // Get today's date
            DateTime today = DateTime.Today;
            
            string query = @"
                SELECT 
                    Status, 
                    COUNT(*) AS Count
                FROM 
                    AttendanceRecords
                WHERE 
                    AttendanceDate = @Today
                GROUP BY 
                    Status";
                    
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Today", today);
                
                try
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string status = reader["Status"].ToString().ToLower();
                            int count = Convert.ToInt32(reader["Count"]);
                            
                            switch (status)
                            {
                                case "present":
                                    summary.present = count;
                                    break;
                                case "absent":
                                    summary.absent = count;
                                    break;
                                case "excused":
                                    summary.excused = count;
                                    break;
                                case "late":
                                    summary.late = count;
                                    break;
                                default:
                                    // Add to present as default
                                    summary.present += count;
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving attendance summary");
                }
            }
            
            return summary;
        }
        
        private async Task<dynamic> GetPerformanceStats(SqlConnection connection)
        {
            dynamic stats = new ExpandoObject();
            stats.averageTestScore = 0;
            stats.highPerformers = 0;
            stats.lowPerformers = 0;
            stats.recentImprovements = 0;
            
            // Check if test submissions table exists
            if (!await TableExists(connection, "ProgrammingTestSubmissions"))
            {
                return stats;
            }
            
            // Get average test score
            string avgQuery = @"
                SELECT AVG(CAST(Score AS FLOAT)) AS AvgScore
                FROM ProgrammingTestSubmissions";
                
            using (var command = new SqlCommand(avgQuery, connection))
            {
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        stats.averageTestScore = Math.Round(Convert.ToDouble(result), 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving average test score");
                }
            }
            
            // Get count of high performers (score >= 80%)
            string highQuery = @"
                SELECT COUNT(DISTINCT StudentId) AS Count
                FROM ProgrammingTestSubmissions
                WHERE Score >= 80";
                
            using (var command = new SqlCommand(highQuery, connection))
            {
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        stats.highPerformers = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving high performers count");
                }
            }
            
            // Get count of low performers (score < 50%)
            string lowQuery = @"
                SELECT COUNT(DISTINCT StudentId) AS Count
                FROM ProgrammingTestSubmissions
                WHERE Score < 50";
                
            using (var command = new SqlCommand(lowQuery, connection))
            {
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        stats.lowPerformers = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving low performers count");
                }
            }
            
            // Get count of students showing recent improvement
            // (current month average score > previous month average score)
            string improvementQuery = @"
                SELECT COUNT(*) AS Count
                FROM (
                    SELECT 
                        StudentId,
                        AVG(CASE WHEN MONTH(SubmissionDate) = MONTH(GETDATE()) THEN Score ELSE NULL END) AS CurrentMonthAvg,
                        AVG(CASE WHEN MONTH(SubmissionDate) = MONTH(DATEADD(MONTH, -1, GETDATE())) THEN Score ELSE NULL END) AS PrevMonthAvg
                    FROM 
                        ProgrammingTestSubmissions
                    WHERE 
                        SubmissionDate >= DATEADD(MONTH, -2, GETDATE())
                    GROUP BY 
                        StudentId
                ) AS StudentAvgs
                WHERE 
                    CurrentMonthAvg > PrevMonthAvg
                    AND CurrentMonthAvg IS NOT NULL
                    AND PrevMonthAvg IS NOT NULL";
                
            using (var command = new SqlCommand(improvementQuery, connection))
            {
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        stats.recentImprovements = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving student improvements count");
                }
            }
            
            return stats;
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