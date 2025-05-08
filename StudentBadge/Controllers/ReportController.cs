using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using Microsoft.AspNetCore.Http;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ReportController : Controller
    {
        private readonly ILogger<ReportController> _logger;
        private readonly string _connectionString;

        public ReportController(ILogger<ReportController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GenerateReport")]
        public async Task<IActionResult> GenerateReport(string reportType, string format = "json")
        {
            try
            {
                if (string.IsNullOrEmpty(reportType))
                {
                    return BadRequest("Report type is required.");
                }
                
                List<IDictionary<string, object>> reportData = null;
                string reportName = "";
                
                switch (reportType.ToLower())
                {
                    case "student":
                        reportData = await GetStudentReportData();
                        reportName = "Student Report";
                        break;
                    case "attendance":
                        reportData = await GetAttendanceReportData();
                        reportName = "Attendance Report";
                        break;
                    case "performance":
                        reportData = await GetPerformanceReportData();
                        reportName = "Student Performance Report";
                        break;
                    case "certificate":
                        reportData = await GetCertificateReportData();
                        reportName = "Certificate Report";
                        break;
                    case "challenge":
                        reportData = await GetChallengeReportData();
                        reportName = "Challenge Report";
                        break;
                    default:
                        return BadRequest("Invalid report type.");
                }
                
                if (format.ToLower() == "excel")
                {
                    return await GenerateExcelReportFile(reportData, reportName, reportType);
                }
                else if (format.ToLower() == "csv")
                {
                    return GenerateCsvReportFile(reportData, reportName);
                }
                else
                {
                    return Json(new { success = true, report = reportData });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Route("GetStudentReport")]
        public async Task<IActionResult> GetStudentReport()
        {
            try
            {
                var studentReport = await GetStudentReportData();
                return Json(new { success = true, report = studentReport });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating student report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<List<IDictionary<string, object>>> GetStudentReportData()
        {
            List<IDictionary<string, object>> students = new List<IDictionary<string, object>>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check which tables exist
                bool studentDetailsExists = await TableExists(connection, "StudentDetails");
                bool usersTableExists = await TableExists(connection, "Users");
                bool studentsTableExists = await TableExists(connection, "Students");
                
                string query;
                
                if (studentDetailsExists && usersTableExists)
                {
                    // Using new schema
                    query = @"
                        SELECT 
                            sd.IdNumber,
                            u.FullName,
                            u.Username,
                            u.Email,
                            sd.Course,
                            sd.Section,
                            sd.YearLevel,
                            sd.Address,
                            sd.ContactNumber,
                            sd.GuardianName,
                            sd.GuardianContact
                        FROM 
                            StudentDetails sd
                        JOIN 
                            Users u ON sd.UserId = u.UserId
                        ORDER BY 
                            u.FullName";
                }
                else if (studentsTableExists)
                {
                    // Using old schema
                    query = @"
                        SELECT 
                            IdNumber,
                            FullName,
                            Username,
                            Email,
                            Course,
                            Section,
                            YearLevel,
                            Address,
                            ContactNumber,
                            GuardianName,
                            GuardianContact
                        FROM 
                            Students
                        ORDER BY 
                            FullName";
                }
                else
                {
                    return students;
                }
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var student = new ExpandoObject() as IDictionary<string, object>;
                            student["idNumber"] = reader["IdNumber"].ToString();
                            student["fullName"] = reader["FullName"].ToString();
                            student["username"] = reader["Username"].ToString();
                            student["email"] = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader["Email"].ToString();
                            student["course"] = reader.IsDBNull(reader.GetOrdinal("Course")) ? null : reader["Course"].ToString();
                            student["section"] = reader.IsDBNull(reader.GetOrdinal("Section")) ? null : reader["Section"].ToString();
                            student["yearLevel"] = reader.IsDBNull(reader.GetOrdinal("YearLevel")) ? null : Convert.ToInt32(reader["YearLevel"]);
                            student["address"] = reader.IsDBNull(reader.GetOrdinal("Address")) ? null : reader["Address"].ToString();
                            student["contactNumber"] = reader.IsDBNull(reader.GetOrdinal("ContactNumber")) ? null : reader["ContactNumber"].ToString();
                            student["guardianName"] = reader.IsDBNull(reader.GetOrdinal("GuardianName")) ? null : reader["GuardianName"].ToString();
                            student["guardianContact"] = reader.IsDBNull(reader.GetOrdinal("GuardianContact")) ? null : reader["GuardianContact"].ToString();
                            
                            students.Add(student);
                        }
                    }
                }
            }
            
            return students;
        }
        
        [HttpGet]
        [Route("GetAttendanceReport")]
        public async Task<IActionResult> GetAttendanceReport(string startDate = null, string endDate = null)
        {
            try
            {
                // Default to current month if dates not provided
                if (string.IsNullOrEmpty(startDate))
                {
                    startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).ToString("yyyy-MM-dd");
                }
                
                if (string.IsNullOrEmpty(endDate))
                {
                    endDate = DateTime.Now.ToString("yyyy-MM-dd");
                }
                
                var attendanceReport = await GetAttendanceReportData(startDate, endDate);
                return Json(new { success = true, report = attendanceReport });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating attendance report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<List<IDictionary<string, object>>> GetAttendanceReportData(string startDate = null, string endDate = null)
        {
            List<IDictionary<string, object>> attendanceRecords = new List<IDictionary<string, object>>();
            
            // Default to current month if dates not provided
            DateTime start = string.IsNullOrEmpty(startDate) ? 
                new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1) : 
                DateTime.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                
            DateTime end = string.IsNullOrEmpty(endDate) ? 
                DateTime.Now : 
                DateTime.ParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the table exists
                if (!await TableExists(connection, "AttendanceRecords"))
                {
                    return attendanceRecords;
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
                            a.AttendanceDate, u.FullName";
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
                            a.AttendanceDate, s.FullName";
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
                            AttendanceDate, StudentId";
                }
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StartDate", start);
                    command.Parameters.AddWithValue("@EndDate", end);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var record = new ExpandoObject() as IDictionary<string, object>;
                            record["id"] = Convert.ToInt32(reader["AttendanceId"]);
                            record["studentId"] = reader["StudentId"].ToString();
                            record["date"] = Convert.ToDateTime(reader["AttendanceDate"]).ToString("yyyy-MM-dd");
                            record["status"] = reader["Status"].ToString();
                            record["notes"] = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader["Notes"].ToString();
                            
                            // Add additional properties if they exist in the result
                            if (HasColumn(reader, "StudentName") && !reader.IsDBNull(reader.GetOrdinal("StudentName")))
                            {
                                record["studentName"] = reader["StudentName"].ToString();
                            }
                            
                            if (HasColumn(reader, "Course") && !reader.IsDBNull(reader.GetOrdinal("Course")))
                            {
                                record["course"] = reader["Course"].ToString();
                            }
                            
                            if (HasColumn(reader, "Section") && !reader.IsDBNull(reader.GetOrdinal("Section")))
                            {
                                record["section"] = reader["Section"].ToString();
                            }
                            
                            attendanceRecords.Add(record);
                        }
                    }
                }
                
                // Add attendance summary
                var summaryRecords = await GetAttendanceSummaryData(connection, start, end);
                if (summaryRecords.Count > 0)
                {
                    foreach (var record in attendanceRecords)
                    {
                        var summaryRecord = summaryRecords.FirstOrDefault(s => 
                            s["studentId"].ToString() == record["studentId"].ToString());
                            
                        if (summaryRecord != null)
                        {
                            record["totalDays"] = summaryRecord["totalDays"];
                            record["presentDays"] = summaryRecord["presentDays"];
                            record["absentDays"] = summaryRecord["absentDays"];
                            record["lateDays"] = summaryRecord["lateDays"];
                            record["attendancePercentage"] = summaryRecord["attendancePercentage"];
                        }
                    }
                }
            }
            
            return attendanceRecords;
        }
        
        private async Task<List<IDictionary<string, object>>> GetAttendanceSummaryData(SqlConnection connection, DateTime startDate, DateTime endDate)
        {
            List<IDictionary<string, object>> summaries = new List<IDictionary<string, object>>();
            
            // Check if the table exists
            if (!await TableExists(connection, "AttendanceRecords"))
            {
                return summaries;
            }
            
            string query = @"
                SELECT 
                    StudentId,
                    COUNT(*) AS TotalDays,
                    SUM(CASE WHEN Status = 'Present' THEN 1 ELSE 0 END) AS PresentDays,
                    SUM(CASE WHEN Status = 'Absent' THEN 1 ELSE 0 END) AS AbsentDays,
                    SUM(CASE WHEN Status = 'Late' THEN 1 ELSE 0 END) AS LateDays
                FROM 
                    AttendanceRecords
                WHERE 
                    AttendanceDate BETWEEN @StartDate AND @EndDate
                GROUP BY 
                    StudentId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StartDate", startDate);
                command.Parameters.AddWithValue("@EndDate", endDate);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var summary = new ExpandoObject() as IDictionary<string, object>;
                        summary["studentId"] = reader["StudentId"].ToString();
                        summary["totalDays"] = Convert.ToInt32(reader["TotalDays"]);
                        summary["presentDays"] = Convert.ToInt32(reader["PresentDays"]);
                        summary["absentDays"] = Convert.ToInt32(reader["AbsentDays"]);
                        summary["lateDays"] = Convert.ToInt32(reader["LateDays"]);
                        
                        // Calculate attendance percentage
                        int totalDays = Convert.ToInt32(reader["TotalDays"]);
                        int presentDays = Convert.ToInt32(reader["PresentDays"]);
                        
                        if (totalDays > 0)
                        {
                            summary["attendancePercentage"] = Math.Round((double)presentDays / totalDays * 100, 2);
                        }
                        else
                        {
                            summary["attendancePercentage"] = 0;
                        }
                        
                        summaries.Add(summary);
                    }
                }
            }
            
            return summaries;
        }
        
        [HttpGet]
        [Route("GetPerformanceReport")]
        public async Task<IActionResult> GetPerformanceReport()
        {
            try
            {
                var performanceReport = await GetPerformanceReportData();
                return Json(new { success = true, report = performanceReport });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating performance report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<List<IDictionary<string, object>>> GetPerformanceReportData()
        {
            List<IDictionary<string, object>> performanceRecords = new List<IDictionary<string, object>>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check which tables exist
                bool testSubmissionsExists = await TableExists(connection, "ProgrammingTestSubmissions");
                bool certificatesExists = await TableExists(connection, "Certificates");
                bool challengeSubmissionsExists = await TableExists(connection, "ChallengeSubmissions");
                
                // Get student list with basic info
                var students = await GetStudentReportData();
                
                // For each student, collect their performance data
                foreach (var student in students)
                {
                    var performanceRecord = new ExpandoObject() as IDictionary<string, object>;
                    performanceRecord["studentId"] = student["idNumber"];
                    performanceRecord["fullName"] = student["fullName"];
                    performanceRecord["course"] = student["course"];
                    performanceRecord["section"] = student["section"];
                    performanceRecord["yearLevel"] = student["yearLevel"];
                    
                    string studentId = student["idNumber"].ToString();
                    
                    // Get test submissions data
                    if (testSubmissionsExists)
                    {
                        var testStats = await GetStudentTestStatsData(connection, studentId);
                        performanceRecord["testCount"] = testStats["testCount"];
                        performanceRecord["averageTestScore"] = testStats["averageScore"];
                        performanceRecord["highestTestScore"] = testStats["highestScore"];
                        performanceRecord["lowestTestScore"] = testStats["lowestScore"];
                    }
                    else
                    {
                        performanceRecord["testCount"] = 0;
                        performanceRecord["averageTestScore"] = 0;
                        performanceRecord["highestTestScore"] = 0;
                        performanceRecord["lowestTestScore"] = 0;
                    }
                    
                    // Get certificate count
                    if (certificatesExists)
                    {
                        string certificateQuery = "SELECT COUNT(*) FROM Certificates WHERE StudentId = @StudentId";
                        using (var command = new SqlCommand(certificateQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            var result = await command.ExecuteScalarAsync();
                            performanceRecord["certificateCount"] = Convert.ToInt32(result);
                        }
                    }
                    else
                    {
                        performanceRecord["certificateCount"] = 0;
                    }
                    
                    // Get challenge submission data
                    if (challengeSubmissionsExists)
                    {
                        var challengeStats = await GetStudentChallengeStatsData(connection, studentId);
                        performanceRecord["challengeCount"] = challengeStats["challengeCount"];
                        performanceRecord["averageChallengeScore"] = challengeStats["averageScore"];
                    }
                    else
                    {
                        performanceRecord["challengeCount"] = 0;
                        performanceRecord["averageChallengeScore"] = 0;
                    }
                    
                    // Calculate overall performance score
                    // 50% test scores, 30% challenges, 20% certificates
                    double testWeight = 0.5;
                    double challengeWeight = 0.3;
                    double certificateWeight = 0.2;
                    
                    double testScore = Convert.ToInt32(performanceRecord["testCount"]) > 0 ? 
                        Convert.ToDouble(performanceRecord["averageTestScore"]) : 0;
                    
                    double challengeScore = Convert.ToInt32(performanceRecord["challengeCount"]) > 0 ? 
                        Convert.ToDouble(performanceRecord["averageChallengeScore"]) : 0;
                    
                    double certificateScore = Convert.ToInt32(performanceRecord["certificateCount"]) * 10; // Each certificate worth 10 points
                    if (certificateScore > 100) certificateScore = 100; // Cap at 100
                    
                    performanceRecord["overallScore"] = Math.Round(
                        (testScore * testWeight) + 
                        (challengeScore * challengeWeight) + 
                        (certificateScore * certificateWeight), 
                        2);
                    
                    // Determine performance level
                    double overallScore = Convert.ToDouble(performanceRecord["overallScore"]);
                    if (overallScore >= 90)
                    {
                        performanceRecord["performanceLevel"] = "Excellent";
                    }
                    else if (overallScore >= 80)
                    {
                        performanceRecord["performanceLevel"] = "Very Good";
                    }
                    else if (overallScore >= 70)
                    {
                        performanceRecord["performanceLevel"] = "Good";
                    }
                    else if (overallScore >= 60)
                    {
                        performanceRecord["performanceLevel"] = "Satisfactory";
                    }
                    else if (overallScore >= 50)
                    {
                        performanceRecord["performanceLevel"] = "Needs Improvement";
                    }
                    else
                    {
                        performanceRecord["performanceLevel"] = "Unsatisfactory";
                    }
                    
                    performanceRecords.Add(performanceRecord);
                }
            }
            
            // Sort by overall score descending
            return performanceRecords.OrderByDescending(r => Convert.ToDouble(r["overallScore"])).ToList();
        }
        
        private async Task<IDictionary<string, object>> GetStudentTestStatsData(SqlConnection connection, string studentId)
        {
            var stats = new ExpandoObject() as IDictionary<string, object>;
            stats["testCount"] = 0;
            stats["averageScore"] = 0;
            stats["highestScore"] = 0;
            stats["lowestScore"] = 0;
            
            string query = @"
                SELECT 
                    COUNT(*) AS TestCount,
                    AVG(Score) AS AvgScore,
                    MAX(Score) AS MaxScore,
                    MIN(Score) AS MinScore
                FROM 
                    ProgrammingTestSubmissions
                WHERE 
                    StudentId = @StudentId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        stats["testCount"] = Convert.ToInt32(reader["TestCount"]);
                        
                        if (Convert.ToInt32(stats["testCount"]) > 0)
                        {
                            stats["averageScore"] = Math.Round(Convert.ToDouble(reader["AvgScore"]), 2);
                            stats["highestScore"] = Convert.ToInt32(reader["MaxScore"]);
                            stats["lowestScore"] = Convert.ToInt32(reader["MinScore"]);
                        }
                    }
                }
            }
            
            return stats;
        }
        
        private async Task<IDictionary<string, object>> GetStudentChallengeStatsData(SqlConnection connection, string studentId)
        {
            var stats = new ExpandoObject() as IDictionary<string, object>;
            stats["challengeCount"] = 0;
            stats["averageScore"] = 0;
            
            string query = @"
                SELECT 
                    COUNT(*) AS ChallengeCount,
                    AVG(PercentageScore) AS AvgScore
                FROM 
                    ChallengeSubmissions
                WHERE 
                    StudentId = @StudentId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        stats["challengeCount"] = Convert.ToInt32(reader["ChallengeCount"]);
                        
                        if (Convert.ToInt32(stats["challengeCount"]) > 0)
                        {
                            stats["averageScore"] = Math.Round(Convert.ToDouble(reader["AvgScore"]), 2);
                        }
                    }
                }
            }
            
            return stats;
        }
        
        [HttpGet]
        [Route("GetCertificateReport")]
        public async Task<IActionResult> GetCertificateReport()
        {
            try
            {
                var certificateReport = await GetCertificateReportData();
                return Json(new { success = true, report = certificateReport });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating certificate report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<List<IDictionary<string, object>>> GetCertificateReportData()
        {
            List<IDictionary<string, object>> certificates = new List<IDictionary<string, object>>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the table exists
                if (!await TableExists(connection, "Certificates"))
                {
                    return certificates;
                }
                
                string query = @"
                    SELECT 
                        c.CertificateId, 
                        c.StudentId,
                        c.StudentName,
                        c.TestId,
                        c.TestName,
                        c.ProgrammingLanguage,
                        c.GradeLevel,
                        c.Score,
                        c.IssueDate
                    FROM 
                        Certificates c
                    ORDER BY 
                        c.IssueDate DESC";
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var certificate = new ExpandoObject() as IDictionary<string, object>;
                            certificate["id"] = Convert.ToInt32(reader["CertificateId"]);
                            certificate["studentId"] = reader["StudentId"].ToString();
                            certificate["studentName"] = reader["StudentName"].ToString();
                            certificate["testId"] = Convert.ToInt32(reader["TestId"]);
                            certificate["testName"] = reader["TestName"].ToString();
                            certificate["programmingLanguage"] = reader["ProgrammingLanguage"].ToString();
                            certificate["gradeLevel"] = Convert.ToInt32(reader["GradeLevel"]);
                            certificate["score"] = Convert.ToInt32(reader["Score"]);
                            certificate["issueDate"] = Convert.ToDateTime(reader["IssueDate"]);
                            
                            certificates.Add(certificate);
                        }
                    }
                }
            }
            
            return certificates;
        }
        
        [HttpGet]
        [Route("GetChallengeReport")]
        public async Task<IActionResult> GetChallengeReport()
        {
            try
            {
                var challengeReport = await GetChallengeReportData();
                return Json(new { success = true, report = challengeReport });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating challenge report");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private async Task<List<IDictionary<string, object>>> GetChallengeReportData()
        {
            List<IDictionary<string, object>> challengeSubmissions = new List<IDictionary<string, object>>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if the tables exist
                if (!await TableExists(connection, "ChallengeSubmissions") || 
                    !await TableExists(connection, "Challenges"))
                {
                    return challengeSubmissions;
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
                            cs.SubmissionId,
                            cs.ChallengeId,
                            ch.ChallengeName,
                            cs.StudentId,
                            u.FullName AS StudentName,
                            sd.Course,
                            sd.Section,
                            cs.SubmissionDate,
                            cs.PercentageScore,
                            cs.PointsEarned,
                            cs.TotalPoints
                        FROM 
                            ChallengeSubmissions cs
                        JOIN 
                            Challenges ch ON cs.ChallengeId = ch.ChallengeId
                        JOIN 
                            Users u ON cs.StudentId = u.UserId
                        JOIN 
                            StudentDetails sd ON u.UserId = sd.UserId
                        ORDER BY 
                            cs.SubmissionDate DESC";
                }
                else if (studentsTableExists)
                {
                    // Using old schema
                    query = @"
                        SELECT 
                            cs.SubmissionId,
                            cs.ChallengeId,
                            ch.ChallengeName,
                            cs.StudentId,
                            s.FullName AS StudentName,
                            s.Course,
                            s.Section,
                            cs.SubmissionDate,
                            cs.PercentageScore,
                            cs.PointsEarned,
                            cs.TotalPoints
                        FROM 
                            ChallengeSubmissions cs
                        JOIN 
                            Challenges ch ON cs.ChallengeId = ch.ChallengeId
                        JOIN 
                            Students s ON cs.StudentId = s.IdNumber
                        ORDER BY 
                            cs.SubmissionDate DESC";
                }
                else
                {
                    // Fallback query with just challenge data
                    query = @"
                        SELECT 
                            cs.SubmissionId,
                            cs.ChallengeId,
                            ch.ChallengeName,
                            cs.StudentId,
                            cs.SubmissionDate,
                            cs.PercentageScore,
                            cs.PointsEarned,
                            cs.TotalPoints
                        FROM 
                            ChallengeSubmissions cs
                        JOIN 
                            Challenges ch ON cs.ChallengeId = ch.ChallengeId
                        ORDER BY 
                            cs.SubmissionDate DESC";
                }
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var submission = new ExpandoObject() as IDictionary<string, object>;
                            submission["id"] = Convert.ToInt32(reader["SubmissionId"]);
                            submission["challengeId"] = Convert.ToInt32(reader["ChallengeId"]);
                            submission["challengeName"] = reader["ChallengeName"].ToString();
                            submission["studentId"] = reader["StudentId"].ToString();
                            submission["submissionDate"] = Convert.ToDateTime(reader["SubmissionDate"]);
                            submission["percentageScore"] = Convert.ToInt32(reader["PercentageScore"]);
                            
                            // Include points earned and total if they exist
                            if (HasColumn(reader, "PointsEarned") && !reader.IsDBNull(reader.GetOrdinal("PointsEarned")))
                            {
                                submission["pointsEarned"] = Convert.ToInt32(reader["PointsEarned"]);
                            }
                            
                            if (HasColumn(reader, "TotalPoints") && !reader.IsDBNull(reader.GetOrdinal("TotalPoints")))
                            {
                                submission["totalPoints"] = Convert.ToInt32(reader["TotalPoints"]);
                            }
                            
                            // Include student details if they exist
                            if (HasColumn(reader, "StudentName") && !reader.IsDBNull(reader.GetOrdinal("StudentName")))
                            {
                                submission["studentName"] = reader["StudentName"].ToString();
                            }
                            
                            if (HasColumn(reader, "Course") && !reader.IsDBNull(reader.GetOrdinal("Course")))
                            {
                                submission["course"] = reader["Course"].ToString();
                            }
                            
                            if (HasColumn(reader, "Section") && !reader.IsDBNull(reader.GetOrdinal("Section")))
                            {
                                submission["section"] = reader["Section"].ToString();
                            }
                            
                            challengeSubmissions.Add(submission);
                        }
                    }
                }
            }
            
            return challengeSubmissions;
        }
        
        private async Task<FileResult> GenerateExcelReportFile(List<IDictionary<string, object>> reportData, string reportName, string reportType)
        {
            // Since we can't use ClosedXML directly, we'll generate a CSV instead
            return GenerateCsvReportFile(reportData, reportName);
        }
        
        private FileContentResult GenerateCsvReportFile(List<IDictionary<string, object>> reportData, string reportName)
        {
            var sb = new StringBuilder();
            
            // Add headers
            if (reportData.Count > 0)
            {
                var properties = reportData[0].Keys.ToList();
                
                sb.AppendLine(string.Join(",", properties.Select(p => $"\"{ToTitleCase(p)}\"")));
                
                // Add data rows
                foreach (var dataItem in reportData)
                {
                    var values = new List<string>();
                    
                    foreach (var prop in properties)
                    {
                        var value = dataItem[prop];
                        values.Add(value != null ? $"\"{value}\"" : "\"\"");
                    }
                    
                    sb.AppendLine(string.Join(",", values));
                }
            }
            
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            
            return File(bytes, "text/csv", $"{reportName}_{DateTime.Now:yyyyMMdd}.csv");
        }
        
        private string ToTitleCase(string str)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                string.Join(" ", 
                    // Insert a space before each capital letter
                    string.Concat(str.Select(c => char.IsUpper(c) ? " " + c : c.ToString()))
                    .TrimStart()
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                )
            );
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
    }
}