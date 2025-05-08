using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StudentBadge.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Dynamic;
using System.Linq;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class AdminDashboardController : Controller
    {
        private readonly ILogger<AdminDashboardController> _logger;
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public AdminDashboardController(ILogger<AdminDashboardController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _configuration = configuration;
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

        [HttpGet]
        [Route("ScoreWeights")]
        public async Task<IActionResult> ScoreWeights()
        {
            try
            {
                // Check if user is admin
                if (HttpContext.Session.GetString("Role") != "admin")
                {
                    return RedirectToAction("Login", "Account");
                }

                var weights = await GetScoreWeights();
                return View(weights);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading score weights");
                ViewBag.Error = "Error loading score weights: " + ex.Message;
                return View(new List<ScoreWeight>());
            }
        }

        [HttpPost]
        [Route("UpdateScoreWeights")]
        public async Task<IActionResult> UpdateScoreWeights([FromForm] WeightsFormViewModel model)
        {
            try
            {
                // Check if user is admin
                if (HttpContext.Session.GetString("Role") != "admin")
                {
                    return RedirectToAction("Login", "Account");
                }

                if (model?.weights == null || !model.weights.Any())
                {
                    TempData["Error"] = "No weights were provided";
                    return RedirectToAction("ScoreWeights");
                }

                var weights = model.weights;

                // Check if weights add up to 100%
                decimal totalWeight = 0;
                foreach (var weight in weights)
                {
                    totalWeight += weight.Weight;
                }

                if (Math.Abs(totalWeight - 100) > 0.1m)
                {
                    TempData["Error"] = "Weights must sum up to 100%. Current total: " + totalWeight;
                    return RedirectToAction("ScoreWeights");
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Capture the old weights BEFORE updating the database
                    decimal oldMasteryWeight = 0;
                    decimal oldChallengesWeight = 0;
                    
                    // Get the old mastery weight before update
                    string getMasteryWeightQuery = @"
                        SELECT Weight FROM ScoreWeights 
                        WHERE CategoryName = 'Mastery'";
                    
                    using (var command = new SqlCommand(getMasteryWeightQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            oldMasteryWeight = Convert.ToDecimal(result);
                            _logger.LogInformation($"Current mastery weight before update: {oldMasteryWeight}%");
                        }
                    }
                    
                    // Get the old challenges weight before update
                    string getChallengesWeightQuery = @"
                        SELECT Weight FROM ScoreWeights 
                        WHERE CategoryName = 'CompletedChallenges'";
                    
                    using (var command = new SqlCommand(getChallengesWeightQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            oldChallengesWeight = Convert.ToDecimal(result);
                            _logger.LogInformation($"Current challenges weight before update: {oldChallengesWeight}%");
                        }
                    }
                    
                    // Start a transaction to update all weights
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Disable the trigger that enforces weights sum to 100
                            // When updating multiple rows, the trigger might fail in intermediate states
                            string disableTriggerSql = "DISABLE TRIGGER TR_ScoreWeights_EnsureTotalIs100 ON ScoreWeights";
                            using (var disableCommand = new SqlCommand(disableTriggerSql, connection, transaction))
                            {
                                await disableCommand.ExecuteNonQueryAsync();
                            }
                            
                            // Update each weight
                            decimal dbTotalWeight = 0;
                            foreach (var weight in weights)
                            {
                                string updateSql = "UPDATE ScoreWeights SET Weight = @Weight WHERE CategoryName = @CategoryName";
                                using (var command = new SqlCommand(updateSql, connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@Weight", weight.Weight);
                                    command.Parameters.AddWithValue("@CategoryName", weight.CategoryName);
                                    await command.ExecuteNonQueryAsync();
                                    
                                    dbTotalWeight += weight.Weight;
                                }
                            }
                            
                            // Verify that weights add up to 100% in the database as well
                            if (weights.Count > 0)
                            {
                                if (Math.Abs(dbTotalWeight - 100) > 0.1m)
                                {
                                    throw new Exception($"Database validation failed: Total weight must equal 100%. Current total: {dbTotalWeight:F2}%");
                                }
                            }

                            // Commit transaction if all updates were successful
                            transaction.Commit();
                            
                            // Find the new mastery weight from the updated weights
                            decimal newMasteryWeight = 0;
                            var masteryWeight = weights.FirstOrDefault(w => w.CategoryName.Equals("Mastery", StringComparison.OrdinalIgnoreCase));
                            if (masteryWeight != null)
                            {
                                newMasteryWeight = masteryWeight.Weight;
                                _logger.LogInformation($"New mastery weight after update: {newMasteryWeight}%");
                            }
                            
                            // Find the new challenges weight from the updated weights
                            decimal newChallengesWeight = 0;
                            var challengesWeight = weights.FirstOrDefault(w => w.CategoryName.Equals("CompletedChallenges", StringComparison.OrdinalIgnoreCase));
                            if (challengesWeight != null)
                            {
                                newChallengesWeight = challengesWeight.Weight;
                                _logger.LogInformation($"New challenges weight after update: {newChallengesWeight}%");
                            }
                            
                            // After committing transaction, first update mastery scores using our new formula
                            // Create ScoreController to access its methods
                            var loggerFactory = HttpContext.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                            var scoreLogger = loggerFactory.CreateLogger<ScoreController>();
                            var scoreController = new ScoreController(_configuration, scoreLogger);
                            
                            // Always update mastery scores with the weight ratio formula
                            // Even if weights seem the same, there might be rounding differences
                            _logger.LogInformation($"Calling UpdateMasteryScoresWhenWeightChanges with oldWeight={oldMasteryWeight}, newWeight={newMasteryWeight}");
                            await scoreController.UpdateMasteryScoresWhenWeightChanges(oldMasteryWeight);
                            
                            // Update challenges scores using the same ratio formula
                            _logger.LogInformation($"Calling UpdateChallengesScoresWhenWeightChanges with oldWeight={oldChallengesWeight}, newWeight={newChallengesWeight}");
                            await scoreController.UpdateChallengesScoresWhenWeightChanges(oldChallengesWeight);
                            
                            // Then recalculate remaining scores for all students
                            await RecalculateAllStudentScores(connection);
                        }
                        catch (Exception ex)
                        {
                            // Rollback transaction if any updates failed
                            transaction.Rollback();
                            throw new Exception("Error updating weights in database: " + ex.Message, ex);
                        }
                        finally
                        {
                            // Re-enable the trigger
                            string enableTriggerSql = "ENABLE TRIGGER TR_ScoreWeights_EnsureTotalIs100 ON ScoreWeights";
                            using (var enableCommand = new SqlCommand(enableTriggerSql, connection))
                            {
                                try
                                {
                                    await enableCommand.ExecuteNonQueryAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Could not re-enable trigger");
                                }
                            }
                        }
                    }
                }

                TempData["Success"] = "Score weights updated successfully and all student scores recalculated";
                return RedirectToAction("ScoreWeights");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating score weights");
                TempData["Error"] = "Error updating score weights: " + ex.Message;
                return RedirectToAction("ScoreWeights");
            }
        }

        private async Task RecalculateAllStudentScores(SqlConnection connection)
        {
            try
            {
                // Get all student IDs
                List<string> studentIds = new List<string>();
                string query = "SELECT UserId FROM StudentDetails";
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            studentIds.Add(reader.GetString(0));
                        }
                    }
                }
                
                // Initialize progress tracking
                // Store in TempData so it can be accessed by other controller actions
                int totalStudents = studentIds.Count;
                int currentStudent = 0;
                
                // Store the progress data in a static dictionary for access by status endpoint
                InitializeProgressTracker(totalStudents);

                // Create ScoreController to access its methods
                // Get the logger factory from DI and create a properly typed logger
                var loggerFactory = HttpContext.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                var scoreLogger = loggerFactory.CreateLogger<ScoreController>();
                var scoreController = new ScoreController(_configuration, scoreLogger);

                // Skip mastery and challenges scores as they are handled separately with their own formulas
                // Both are updated using their respective UpdateXXXScoresWhenWeightChanges methods with the ratio formula
                _logger.LogInformation("Skipping mastery and challenges scores as they are handled separately with ratio formulas");
                
                // Recalculate remaining scores for each student
                int updatedCount = 0;
                foreach (var studentId in studentIds)
                {
                    if (!string.IsNullOrEmpty(studentId))
                    {
                        currentStudent++;
                        // Update progress tracker
                        UpdateProgressTracker(currentStudent, studentId);
                        
                        // Recalculate other component scores
                        await scoreController.CalculateSeminarsScoreFromAttendance(studentId);
                        await scoreController.CalculateAcademicGradesScore(studentId);
                        await scoreController.CalculateExtracurricularScoreFromActivities(studentId);
                        
                        // Calculate overall score based on all updated component scores
                        // Use CalculateAllScoresForStudent instead of CalculateOverallScore
                        await scoreController.CalculateAllScoresForStudent(studentId);
                        
                        updatedCount++;
                    }
                }

                // Mark the process as complete
                CompleteProgressTracker();
                _logger.LogInformation($"Recalculated scores for {updatedCount} students after weight update");
            }
            catch (Exception ex)
            {
                // Mark the process as failed in case of error
                FailProgressTracker(ex.Message);
                _logger.LogError(ex, "Error recalculating all student scores");
                throw;
            }
        }

        // Static dictionary to hold progress tracking information
        private static Dictionary<string, object> _progressTracker = new Dictionary<string, object>();

        // Initialize the progress tracker
        private void InitializeProgressTracker(int totalStudents)
        {
            lock (_progressTracker)
            {
                _progressTracker["TotalStudents"] = totalStudents;
                _progressTracker["CurrentStudent"] = 0;
                _progressTracker["CurrentStudentId"] = "";
                _progressTracker["IsComplete"] = false;
                _progressTracker["IsError"] = false;
                _progressTracker["ErrorMessage"] = "";
                _progressTracker["StartTime"] = DateTime.Now;
            }
        }

        // Update the progress tracker with current status
        private void UpdateProgressTracker(int currentStudent, string studentId)
        {
            lock (_progressTracker)
            {
                _progressTracker["CurrentStudent"] = currentStudent;
                _progressTracker["CurrentStudentId"] = studentId;
            }
        }

        // Mark the process as complete
        private void CompleteProgressTracker()
        {
            lock (_progressTracker)
            {
                _progressTracker["IsComplete"] = true;
                _progressTracker["EndTime"] = DateTime.Now;
            }
        }

        // Mark the process as failed
        private void FailProgressTracker(string errorMessage)
        {
            lock (_progressTracker)
            {
                _progressTracker["IsError"] = true;
                _progressTracker["ErrorMessage"] = errorMessage;
                _progressTracker["EndTime"] = DateTime.Now;
            }
        }

        [HttpGet]
        [Route("GetProgressStatus")]
        public IActionResult GetProgressStatus()
        {
            try
            {
                // Copy the tracker data to avoid potential thread issues
                Dictionary<string, object> progressData;
                lock (_progressTracker)
                {
                    progressData = new Dictionary<string, object>(_progressTracker);
                }
                
                return Json(new { success = true, progress = progressData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting progress status");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<List<ScoreWeight>> GetScoreWeights()
        {
            var weights = new List<ScoreWeight>();
            
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                // Check if ScoreWeights table exists
                bool tableExists = await TableExists(connection, "ScoreWeights");
                
                if (!tableExists)
                {
                    // Create default weights if table doesn't exist
                    weights.Add(new ScoreWeight { Id = 1, CategoryName = "AcademicGrades", Weight = 30, Description = "Weight for academic grades scores" });
                    weights.Add(new ScoreWeight { Id = 2, CategoryName = "CompletedChallenges", Weight = 20, Description = "Weight for completed challenges scores" });
                    weights.Add(new ScoreWeight { Id = 3, CategoryName = "Mastery", Weight = 20, Description = "Weight for mastery scores" });
                    weights.Add(new ScoreWeight { Id = 4, CategoryName = "SeminarsWebinars", Weight = 10, Description = "Weight for seminars and webinars scores" });
                    weights.Add(new ScoreWeight { Id = 5, CategoryName = "Extracurricular", Weight = 20, Description = "Weight for extracurricular activities scores" });
                    
                    return weights;
                }
                
                string query = "SELECT Id, CategoryName, Weight, Description, CreatedDate, ModifiedDate FROM ScoreWeights ORDER BY Id";
                
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            weights.Add(new ScoreWeight
                            {
                                Id = reader.GetInt32(0),
                                CategoryName = reader.GetString(1),
                                Weight = reader.GetDecimal(2),
                                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                                CreatedDate = reader.IsDBNull(4) ? DateTime.Now : reader.GetDateTime(4),
                                ModifiedDate = reader.IsDBNull(5) ? DateTime.Now : reader.GetDateTime(5)
                            });
                        }
                    }
                }
            }
            
            return weights;
        }
    }
}