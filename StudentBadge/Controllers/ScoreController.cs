using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ScoreController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScoreController> _logger;

        public ScoreController(IConfiguration configuration, ILogger<ScoreController> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Helper method to get the seminar cap value
        private async Task<int> GetSeminarCap(SqlConnection connection)
        {
            int defaultCap = 10; // Default cap of 10 seminars at 1 point each
            
            try
            {
                // Check if the ApplicationSettings table exists
                bool tableExists = await TableExists(connection, "ApplicationSettings");
                if (!tableExists)
                {
                    _logger.LogWarning("ApplicationSettings table doesn't exist. Using default seminar cap of 10.");
                    return defaultCap;
                }

                // Check if SeminarCap setting exists
                string query = "SELECT SettingValue FROM ApplicationSettings WHERE SettingName = 'SeminarCap'";
                using (var command = new SqlCommand(query, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        int cap = Convert.ToInt32(result);
                        _logger.LogInformation($"Found seminar cap setting: {cap}");
                        return cap;
                    }
                    else
                    {
                        // Create the setting if it doesn't exist
                        _logger.LogInformation("SeminarCap setting not found, creating with default value 10");
                        string insertQuery = "INSERT INTO ApplicationSettings (SettingName, SettingValue) VALUES ('SeminarCap', @DefaultCap)";
                        using (var insertCommand = new SqlCommand(insertQuery, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@DefaultCap", defaultCap);
                            await insertCommand.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving seminar cap. Using default value of 10.");
            }
            
            return defaultCap;
        }

        [HttpPost]
        [Route("CalculateSeminarsScoreFromAttendance")]
        public async Task<IActionResult> CalculateSeminarsScoreFromAttendance(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateSeminarsScoreFromAttendance");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating seminar score for student {studentId} based on attendance records");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

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

                    // Check if AttendanceRecords table exists
                    if (!await TableExists(connection, "AttendanceRecords"))
                    {
                        _logger.LogWarning("AttendanceRecords table does not exist. Setting seminar score to 0.");
                        await UpdateSeminarScoreValue(connection, studentId, 0);
                        return Json(new { success = true, message = "No attendance records. Seminar score set to 0." });
                    }

                    // First try with the original studentId
                    string countCheckQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                    int recordCount = 0;
                    
                    using (var command = new SqlCommand(countCheckQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        recordCount = (int)await command.ExecuteScalarAsync();
                        _logger.LogInformation($"Found {recordCount} attendance records with direct StudentId {studentId}");
                    }
                    
                    // If no records found, try with the resolved UserId if available
                    if (recordCount == 0 && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        using (var command = new SqlCommand(countCheckQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", resolvedUserId);
                            recordCount = (int)await command.ExecuteScalarAsync();
                            _logger.LogInformation($"Found {recordCount} attendance records with resolved UserId {resolvedUserId}");
                            
                            // If we found records with the resolved ID, use that for further queries
                            if (recordCount > 0)
                            {
                                studentId = resolvedUserId;
                            }
                        }
                    }
                    
                    if (recordCount == 0)
                    {
                        _logger.LogWarning($"No attendance records found for student {studentId}. Setting seminar score to 0.");
                        await UpdateSeminarScoreValue(connection, studentId, 0);
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { success = true, message = "No attendance records found. Seminar score set to 0." });
                    }

                    // Get the score weight from the database
                    Dictionary<string, decimal> weights = await GetScoreWeights(connection);
                    decimal seminarWeight = weights["SeminarsWebinars"];
                    _logger.LogInformation($"Using seminar weight: {seminarWeight}%");

                    // Check if Score column exists in AttendanceRecords
                    if (!await ColumnExists(connection, "AttendanceRecords", "Score"))
                    {
                        _logger.LogWarning("Score column does not exist in AttendanceRecords. Using default value of 100.");
                        
                        // Get the total count of attendance records for the student
                        string countQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                        int totalAttendance = 0;
                        
                        using (var command = new SqlCommand(countQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            totalAttendance = (int)await command.ExecuteScalarAsync();
                        }
                        
                        // Calculate weighted score directly: (SeminarsAttended / 10) * ScoreWeight
                        // Cap the raw portion at 100% (meaning 10 or more seminars gives full credit)
                        decimal rawScore = Math.Min(totalAttendance / 10.0m, 1.0m);
                        decimal seminarScore = rawScore * seminarWeight;
                        
                        // Update the score with the weighted value
                        bool updateSuccess = await UpdateSeminarScoreValue(connection, studentId, seminarScore);
                        
                        if (!updateSuccess)
                        {
                            _logger.LogWarning($"Failed to update seminar score for student {studentId}");
                            return Json(new { success = false, message = "Failed to update seminar score." });
                        }
                        
                        _logger.LogInformation($"Updated seminar score for student {studentId} to {seminarScore} based on {totalAttendance} attendance records (formula: {totalAttendance} / 10 * {seminarWeight})");
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { 
                            success = true, 
                            message = $"Seminar score updated to {seminarScore} based on {totalAttendance} attendance records." 
                        });
                    }
                    else
                    {
                        // Count the number of seminars attended
                        string countQuery = "SELECT COUNT(*) FROM AttendanceRecords WHERE StudentId = @StudentId";
                        int seminarsAttended = 0;
                        
                        using (var command = new SqlCommand(countQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            seminarsAttended = (int)await command.ExecuteScalarAsync();
                            _logger.LogInformation($"Total seminars attended for student {studentId}: {seminarsAttended}");
                        }
                        
                        // Calculate weighted score directly: (SeminarsAttended / 10) * ScoreWeight
                        // Cap the raw portion at 100% (meaning 10 or more seminars gives full credit)
                        decimal rawScore = Math.Min(seminarsAttended / 10.0m, 1.0m);
                        decimal seminarScore = rawScore * seminarWeight;
                        
                        // Update the score with the weighted value
                        bool updateSuccess = await UpdateSeminarScoreValue(connection, studentId, seminarScore);
                        
                        if (!updateSuccess)
                        {
                            _logger.LogWarning($"Failed to update seminar score for student {studentId}");
                            return Json(new { success = false, message = "Failed to update seminar score." });
                        }
                        
                        _logger.LogInformation($"Updated seminar score for student {studentId} to {seminarScore} based on {seminarsAttended} seminars attended (formula: {seminarsAttended} / 10 * {seminarWeight})");
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { 
                            success = true, 
                            message = $"Seminar score updated to {seminarScore} based on {seminarsAttended} seminars attended." 
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating seminar score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        // Helper method to resolve UserId from StudentId
        private async Task<string> ResolveUserIdFromStudentId(SqlConnection connection, string studentId)
        {
            try
            {
                // First check if StudentId is already a UserId
                string checkUserIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @StudentId";
                using (var command = new SqlCommand(checkUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int count = (int)await command.ExecuteScalarAsync();
                    if (count > 0)
                    {
                        // studentId is already a valid UserId
                        return studentId;
                    }
                }
                
                // Otherwise, try to get the UserId from IdNumber
                string getUserIdQuery = "SELECT UserId FROM StudentDetails WHERE IdNumber = @StudentId";
                using (var command = new SqlCommand(getUserIdQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                }
                
                // If we couldn't resolve it, return empty string
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resolving UserId for StudentId {studentId}");
                return string.Empty;
            }
        }

        [HttpPost]
        [Route("CalculateExtracurricularScoreFromActivities")]
        public async Task<IActionResult> CalculateExtracurricularScoreFromActivities(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateExtracurricularScoreFromActivities");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating extracurricular score for student {studentId} based on activities");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has a ExtracurricularScore column
                    if (!await ColumnExists(connection, "StudentDetails", "ExtracurricularScore"))
                    {
                        _logger.LogWarning("ExtracurricularScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD ExtracurricularScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created ExtracurricularScore column with default value 0");
                        }
                    }

                    // Check if ExtraCurricularActivities table exists
                    if (!await TableExists(connection, "ExtraCurricularActivities"))
                    {
                        _logger.LogWarning("ExtraCurricularActivities table does not exist. Setting extracurricular score to 0.");
                        await UpdateExtracurricularScoreValue(connection, studentId, 0);
                        
                        // Update the overall student score
                        await RecalculateScore(connection, studentId);
                        
                        return Json(new { success = true, message = "No extracurricular activities. Score set to 0." });
                    }

                    // First try with the original studentId
                    string countQuery = "SELECT COUNT(*) FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                    int activityCount = 0;
                    
                    using (var command = new SqlCommand(countQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        activityCount = (int)await command.ExecuteScalarAsync();
                        _logger.LogInformation($"Found {activityCount} extracurricular activities with direct StudentId {studentId}");
                    }
                    
                    // If no activities found, try with the resolved UserId if available
                    if (activityCount == 0 && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        using (var command = new SqlCommand(countQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", resolvedUserId);
                            activityCount = (int)await command.ExecuteScalarAsync();
                            _logger.LogInformation($"Found {activityCount} extracurricular activities with resolved UserId {resolvedUserId}");
                            
                            // If we found activities with the resolved ID, use that for further queries
                            if (activityCount > 0)
                            {
                                studentId = resolvedUserId;
                            }
                        }
                    }
                    
                    // Calculate extracurricular score
                    decimal activityScore = 0;
                    
                    if (activityCount > 0)
                    {
                        // Get average score from extracurricular activities
                        string avgQuery = "SELECT COALESCE(AVG(Score), 0) FROM ExtraCurricularActivities WHERE StudentId = @StudentId";
                        decimal averageScore = 0;
                        
                        using (var command = new SqlCommand(avgQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                averageScore = Convert.ToDecimal(result);
                                _logger.LogInformation($"Average extracurricular score for student {studentId}: {averageScore}");
                            }
                            else
                            {
                                _logger.LogWarning($"No extracurricular scores found for student {studentId}");
                            }
                        }
                        
                        // Apply multiplier based on the number of activities
                        decimal multiplier = 0.25m; // Default for 1 activity
                        
                        if (activityCount >= 4)
                            multiplier = 1.00m; // 4 or more activities
                        else if (activityCount == 3)
                            multiplier = 0.75m; // 3 activities
                        else if (activityCount == 2)
                            multiplier = 0.50m; // 2 activities
                        
                        // Apply the multiplier to the average score
                        decimal multipliedScore = averageScore * multiplier;
                        
                        _logger.LogInformation($"Applied extracurricular multiplier: {activityCount} activities = {multiplier} multiplier");
                        _logger.LogInformation($"Extracurricular calculation: {averageScore} × {multiplier} = {multipliedScore}");
                        
                        // Get score weight from the database
                        var scoreWeights = await GetScoreWeights(connection);
                        decimal extracurricularWeight = scoreWeights.ContainsKey("Extracurricular") ? 
                                                      scoreWeights["Extracurricular"] : 20m; // Default to 20% if not specified
                        
                        // Apply the weight to get the final score
                        activityScore = multipliedScore * (extracurricularWeight / 100m);
                        
                        _logger.LogInformation($"Applied weight: {multipliedScore} × ({extracurricularWeight}/100) = {activityScore}");
                    }
                    
                    _logger.LogInformation($"Calculated extracurricular score for student {studentId} to {activityScore} based on {activityCount} activities");
                    
                    // Update the score
                    bool updateSuccess = await UpdateExtracurricularScoreValue(connection, studentId, activityScore);
                    
                    if (!updateSuccess)
                    {
                        _logger.LogWarning($"Failed to update extracurricular score for student {studentId}");
                        return Json(new { success = false, message = "Failed to update extracurricular score." });
                    }
                    
                    // Update the overall student score
                    await RecalculateScore(connection, studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Extracurricular score updated to {activityScore} based on {activityCount} activities." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating extracurricular score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("CalculateAllScoresForStudent")]
        public async Task<IActionResult> CalculateAllScoresForStudent(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateAllScoresForStudent");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating all scores for student {studentId}");
                
                bool anySuccess = false;
                string results = "";

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists with UserId or IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Calculate academic score
                    try
                    {
                        var academicResult = await CalculateAcademicGradesScore(studentId);
                        if (academicResult is JsonResult jsonAcademicResult && 
                            jsonAcademicResult.Value is System.Dynamic.ExpandoObject expandoAcademic)
                        {
                            IDictionary<string, object> resultDict = expandoAcademic as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Academic: {message}; ";
                            _logger.LogInformation($"Academic score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating academic score for student {StudentId}", studentId);
                        results += "Academic: Error; ";
                    }
                    
                    // Calculate seminar score
                    try
                    {
                        var seminarResult = await CalculateSeminarsScoreFromAttendance(studentId);
                        if (seminarResult is JsonResult jsonSeminarResult && 
                            jsonSeminarResult.Value is System.Dynamic.ExpandoObject expandoSeminar)
                        {
                            IDictionary<string, object> resultDict = expandoSeminar as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Seminar: {message}; ";
                            _logger.LogInformation($"Seminar score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating seminar score for student {StudentId}", studentId);
                        results += "Seminar: Error; ";
                    }
                    
                    // Calculate extracurricular score
                    try
                    {
                        var extracurricularResult = await CalculateExtracurricularScoreFromActivities(studentId);
                        if (extracurricularResult is JsonResult jsonExtraResult && 
                            jsonExtraResult.Value is System.Dynamic.ExpandoObject expandoExtra)
                        {
                            IDictionary<string, object> resultDict = expandoExtra as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Extracurricular: {message}; ";
                            _logger.LogInformation($"Extracurricular score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating extracurricular score for student {StudentId}", studentId);
                        results += "Extracurricular: Error; ";
                    }
                    
                    // Calculate mastery score
                    try
                    {
                        var masteryResult = await CalculateMasteryScore(studentId);
                        if (masteryResult is JsonResult jsonMasteryResult && 
                            jsonMasteryResult.Value is System.Dynamic.ExpandoObject expandoMastery)
                        {
                            IDictionary<string, object> resultDict = expandoMastery as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Mastery: {message}; ";
                            _logger.LogInformation($"Mastery score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating mastery score for student {StudentId}", studentId);
                        results += "Mastery: Error; ";
                    }
                    
                    // Calculate completed challenges score
                    try
                    {
                        var challengesResult = await CalculateCompletedChallengesScore(studentId);
                        if (challengesResult is JsonResult jsonChallengesResult && 
                            jsonChallengesResult.Value is System.Dynamic.ExpandoObject expandoChallenges)
                        {
                            IDictionary<string, object> resultDict = expandoChallenges as IDictionary<string, object>;
                            bool success = Convert.ToBoolean(resultDict["success"]);
                            string message = resultDict["message"].ToString();
                            
                            if (success) anySuccess = true;
                            results += $"Challenges: {message}; ";
                            _logger.LogInformation($"Challenges score calculation: {message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating challenges score for student {StudentId}", studentId);
                        results += "Challenges: Error; ";
                    }
                    
                    // Ensure the overall score is recalculated
                    if (anySuccess)
                    {
                        try
                        {
                            await RecalculateScore(connection, studentId);
                            _logger.LogInformation($"Successfully recalculated overall score for student {studentId}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error recalculating overall score for student {StudentId}", studentId);
                        }
                    }
                    
                    // Return the result
                    return Json(new { 
                        success = anySuccess, 
                        message = anySuccess ? 
                            $"Scores calculated for student {studentId}: {results}" : 
                            $"Failed to calculate any scores for student {studentId}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating all scores for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpPost]
        [Route("UpdateAttendanceScores")]
        public async Task<IActionResult> UpdateAttendanceScores(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateAttendanceScores");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating attendance-based scores for student {studentId}");
                
                // This method combines seminar score calculation from attendance
                // with academic grades score calculation
                
                // First update the seminar score
                var seminarResult = await CalculateSeminarsScoreFromAttendance(studentId);
                
                // Debug current state of academic score before update
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string getScoreQuery = "SELECT COALESCE(AcademicGradesScore, 0) AS CurrentScore FROM StudentDetails WHERE UserId = @StudentId";
                    decimal currentScore = 0;
                    
                    using (var command = new SqlCommand(getScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            currentScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Current AcademicGradesScore before update: {currentScore}");
                        }
                        else
                        {
                            _logger.LogWarning($"No current AcademicGradesScore found for student {studentId}");
                        }
                    }
                    
                    // Check if we have any year grades that could contribute to the score
                    string getYearGradesQuery = @"
                        SELECT 
                            COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                            COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                            COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                            COALESCE(FourthYearGrade, 0) as FourthYearGrade
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                        
                    using (var command = new SqlCommand(getYearGradesQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                decimal firstYearGrade = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                decimal secondYearGrade = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                decimal thirdYearGrade = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                decimal fourthYearGrade = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                                
                                _logger.LogInformation($"Year grades found: Y1: {firstYearGrade}, " +
                                                     $"Y2: {secondYearGrade}, " +
                                                     $"Y3: {thirdYearGrade}, " +
                                                     $"Y4: {fourthYearGrade}");
                            }
                            else
                            {
                                _logger.LogWarning($"No year grade data found for student {studentId}");
                            }
                        }
                    }
                }
                
                // Update academic grades score
                var academicResult = await CalculateAcademicGradesScore(studentId);
                
                // Check if the update was successful
                bool academicSuccess = false;
                if (academicResult is JsonResult jsonResult && 
                    jsonResult.Value is System.Dynamic.ExpandoObject expandoObj)
                {
                    IDictionary<string, object> resultDict = expandoObj as IDictionary<string, object>;
                    if (resultDict != null && resultDict.ContainsKey("success"))
                    {
                        academicSuccess = Convert.ToBoolean(resultDict["success"]);
                        _logger.LogInformation($"Academic score update result: {academicSuccess}, Message: {resultDict["message"]}");
                    }
                }
                
                // Update the overall student score
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    await RecalculateScore(connection, studentId);
                    
                    // Debug final state of academic score after update
                    string getScoreQuery = "SELECT COALESCE(AcademicGradesScore, 0) AS UpdatedScore FROM StudentDetails WHERE UserId = @StudentId";
                    decimal updatedScore = 0;
                    
                    using (var command = new SqlCommand(getScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            updatedScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Updated AcademicGradesScore after update: {updatedScore}");
                        }
                    }
                }
                
                return Json(new { 
                    success = true, 
                    message = $"Attendance and academic scores updated successfully for student {studentId}." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating attendance scores for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("CalculateAcademicGradesScore")]
        public async Task<IActionResult> CalculateAcademicGradesScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateAcademicGradesScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating academic grades score for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Get the dynamic weights from the ScoreWeights table
                    Dictionary<string, decimal> weights = await GetScoreWeights(connection);
                    decimal academicWeight = weights["AcademicGrades"];
                    _logger.LogInformation($"Using academic weight: {academicWeight}%");

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                        // We'll keep using studentId for queries, but we have resolvedUserId if needed
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        // Try with the resolved UserId as a last resort
                        string checkResolvedIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(checkResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int count = (int)await command.ExecuteScalarAsync();
                            if (count > 0)
                            {
                                studentExists = true;
                                studentId = resolvedUserId; // Use resolved ID for subsequent queries
                                _logger.LogInformation($"Using resolved UserId {resolvedUserId} for academic grade calculations");
                            }
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has an AcademicGradesScore column
                    if (!await ColumnExists(connection, "StudentDetails", "AcademicGradesScore"))
                    {
                        _logger.LogWarning("AcademicGradesScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD AcademicGradesScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created AcademicGradesScore column with default value 0");
                        }
                    }

                    // Get the student's test scores from the StudentDetails table
                    decimal firstYearScore = 0;
                    decimal secondYearScore = 0;
                    decimal thirdYearScore = 0;
                    decimal fourthYearScore = 0;
                    
                    string getTestScoresQuery = @"
                        SELECT 
                            COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                            COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                            COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                            COALESCE(FourthYearGrade, 0) as FourthYearGrade
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                    
                    // First try with direct student ID
                    using (var command = new SqlCommand(getTestScoresQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Check if all the required columns exist
                                bool hasFirstYearGrade = reader.GetOrdinal("FirstYearGrade") >= 0;
                                bool hasSecondYearGrade = reader.GetOrdinal("SecondYearGrade") >= 0;
                                bool hasThirdYearGrade = reader.GetOrdinal("ThirdYearGrade") >= 0;
                                bool hasFourthYearGrade = reader.GetOrdinal("FourthYearGrade") >= 0;
                                
                                if (hasFirstYearGrade) firstYearScore = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                if (hasSecondYearGrade) secondYearScore = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                if (hasThirdYearGrade) thirdYearScore = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                if (hasFourthYearGrade) fourthYearScore = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                            }
                        }
                    }
                    
                    // If no data found, try again with IdNumber query
                    if ((firstYearScore == 0 && secondYearScore == 0 && thirdYearScore == 0 && fourthYearScore == 0))
                    {
                        string getScoresByIdQuery = @"
                            SELECT 
                                COALESCE(FirstYearGrade, 0) as FirstYearGrade,
                                COALESCE(SecondYearGrade, 0) as SecondYearGrade,
                                COALESCE(ThirdYearGrade, 0) as ThirdYearGrade,
                                COALESCE(FourthYearGrade, 0) as FourthYearGrade
                            FROM StudentDetails 
                            WHERE IdNumber = @StudentId";
                        
                        using (var command = new SqlCommand(getScoresByIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // We found the student with IdNumber
                                    _logger.LogInformation($"Found grade data using IdNumber {studentId}");
                                    
                                    bool hasFirstYearGrade = reader.GetOrdinal("FirstYearGrade") >= 0;
                                    bool hasSecondYearGrade = reader.GetOrdinal("SecondYearGrade") >= 0;
                                    bool hasThirdYearGrade = reader.GetOrdinal("ThirdYearGrade") >= 0;
                                    bool hasFourthYearGrade = reader.GetOrdinal("FourthYearGrade") >= 0;
                                    
                                    if (hasFirstYearGrade) firstYearScore = reader.GetDecimal(reader.GetOrdinal("FirstYearGrade"));
                                    if (hasSecondYearGrade) secondYearScore = reader.GetDecimal(reader.GetOrdinal("SecondYearGrade"));
                                    if (hasThirdYearGrade) thirdYearScore = reader.GetDecimal(reader.GetOrdinal("ThirdYearGrade"));
                                    if (hasFourthYearGrade) fourthYearScore = reader.GetDecimal(reader.GetOrdinal("FourthYearGrade"));
                                }
                            }
                        }
                    }
                    
                    // Calculate the academic grades score using the correct formula:
                    // academicgrades = (totalgrades / 4) * scoreweight
                    decimal totalYearGrades = firstYearScore + secondYearScore + thirdYearScore + fourthYearScore;
                    decimal averageGrade = totalYearGrades / 4;
                    decimal academicScore = averageGrade * (academicWeight / 100.0m);
                    
                    _logger.LogInformation($"Student {studentId}: First year: {firstYearScore}, Second year: {secondYearScore}, " +
                                          $"Third year: {thirdYearScore}, Fourth year: {fourthYearScore}, " +
                                          $"Total: {totalYearGrades}, Average: {averageGrade}, " +
                                          $"Academic score = ({averageGrade} * {academicWeight}%) = {academicScore}");
                    
                    // Update the StudentDetails table - try with all possible ID variants
                    bool updated = false;
                    
                    // First try with UserId
                    string updateQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE UserId = @StudentId";
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@AcademicScore", academicScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated AcademicGradesScore for student with UserId {studentId}: {academicScore}");
                        }
                    }
                    
                    // If not updated, try with IdNumber
                    if (!updated)
                    {
                        string updateByIdQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(updateByIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@AcademicScore", academicScore);
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                updated = true;
                                _logger.LogInformation($"Updated AcademicGradesScore by IdNumber for student {studentId}: {academicScore}");
                            }
                        }
                    }
                    
                    // If both failed and we have a resolved ID, try that
                    if (!updated && !string.IsNullOrEmpty(resolvedUserId) && resolvedUserId != studentId)
                    {
                        string updateByResolvedIdQuery = "UPDATE StudentDetails SET AcademicGradesScore = @AcademicScore WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(updateByResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@AcademicScore", academicScore);
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            
                            if (rowsAffected > 0)
                            {
                                updated = true;
                                _logger.LogInformation($"Updated AcademicGradesScore using resolved UserId {resolvedUserId}: {academicScore}");
                            }
                        }
                    }
                    
                    if (!updated)
                    {
                        _logger.LogWarning($"Failed to update AcademicGradesScore for student {studentId}. No matching record found.");
                        return Json(new { success = false, message = "Failed to update academic score." });
                    }
                    
                    // Update the overall student score
                    await RecalculateScore(connection, studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Academic grades score updated to {academicScore} based on {totalYearGrades} year grades.",
                        academicScore = academicScore
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating academic grades score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("AddTestScore")]
        public async Task<IActionResult> AddTestScore(string studentId, int year, decimal score)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for AddTestScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                if (year < 1 || year > 4)
                {
                    _logger.LogError($"Invalid year value: {year}. Must be between 1 and 4.");
                    return Json(new { success = false, message = "Year must be between 1 and 4." });
                }

                if (score < 0 || score > 10)
                {
                    _logger.LogError($"Invalid score value: {score}. Must be between 0 and 10.");
                    return Json(new { success = false, message = "Score must be between 0 and 10." });
                }

                _logger.LogInformation($"Adding test score for student {studentId}, year {year}, score {score}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists
                    if (!await StudentExists(connection, studentId))
                    {
                        _logger.LogWarning($"Student {studentId} not found in database");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    string yearColumn = "";
                    
                    switch (year)
                    {
                        case 1:
                            yearColumn = "FirstYearGrade";
                            break;
                        case 2:
                            yearColumn = "SecondYearGrade";
                            break;
                        case 3:
                            yearColumn = "ThirdYearGrade";
                            break;
                        case 4:
                            yearColumn = "FourthYearGrade";
                            break;
                    }

                    // Ensure columns exist
                    if (!await ColumnExists(connection, "StudentDetails", yearColumn))
                    {
                        _logger.LogWarning($"{yearColumn} column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            $"ALTER TABLE StudentDetails ADD {yearColumn} decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Created {yearColumn} column with default value 0");
                        }
                    }

                    // Update the test score
                    string updateQuery = $"UPDATE StudentDetails SET {yearColumn} = @Score WHERE UserId = @StudentId";
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", score);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        _logger.LogInformation($"Updated {yearColumn} for student {studentId} to {score} (affected rows: {rowsAffected})");
                        
                        // If no rows were affected, try using IdNumber
                        if (rowsAffected == 0)
                        {
                            string updateByIdQuery = $"UPDATE StudentDetails SET {yearColumn} = @Score WHERE IdNumber = @StudentId";
                            using (var idCommand = new SqlCommand(updateByIdQuery, connection))
                            {
                                idCommand.Parameters.AddWithValue("@Score", score);
                                idCommand.Parameters.AddWithValue("@StudentId", studentId);
                                rowsAffected = await idCommand.ExecuteNonQueryAsync();
                                
                                _logger.LogInformation($"Updated {yearColumn} by IdNumber for student {studentId} to {score} (affected rows: {rowsAffected})");
                            }
                        }
                    }
                    
                    // Recalculate academic score
                    var academicResult = await CalculateAcademicGradesScore(studentId);
                    
                    return Json(new { 
                        success = true, 
                        message = $"Test score for year {year} updated to {score} and academic score recalculated." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding test score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Recalculate the overall score for the student
        private async Task RecalculateScore(SqlConnection connection, string studentId)
        {
            try
            {
                _logger.LogInformation($"Starting to recalculate overall score for student {studentId}");
                
                // First, ensure all required columns exist
                await EnsureScoreColumnsExist(connection);
                
                // Get all score components from StudentDetails with correct column names
                string query = @"
                    SELECT 
                        COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                        COALESCE(ExtracurricularScore, 0) as ExtracurricularScore, 
                        COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                        COALESCE(MasteryScore, 0) as MasteryScore,
                        COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore
                    FROM StudentDetails 
                    WHERE UserId = @StudentId OR IdNumber = @StudentId";
                
                decimal academicScore = 0;
                decimal extracurricularScore = 0;
                decimal seminarScore = 0;
                decimal masteryScore = 0;
                decimal challengesScore = 0;
                bool foundScores = false;
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            academicScore = reader.GetDecimal(0);
                            extracurricularScore = reader.GetDecimal(1);
                            seminarScore = reader.GetDecimal(2);
                            masteryScore = reader.GetDecimal(3);
                            challengesScore = reader.GetDecimal(4);
                            foundScores = true;
                            
                            _logger.LogInformation($"Found existing scores for student {studentId}: " +
                                $"Academic={academicScore}, " +
                                $"Extracurricular={extracurricularScore}, " +
                                $"Seminars={seminarScore}, " +
                                $"Mastery={masteryScore}, " +
                                $"Challenges={challengesScore}");
                        }
                    }
                }
                
                if (!foundScores)
                {
                    _logger.LogWarning($"No student record found for ID {studentId}");
                    return;
                }
                
                // Get weights from the database
                var weights = await GetScoreWeights(connection);
                
                // Log the existing component scores that we're using for DB storage
                _logger.LogInformation($"Using existing component scores for DB storage: " +
                    $"Mastery: {masteryScore} (already weighted), Challenges: {challengesScore} (already weighted)");
                
                // Calculate overall score as the sum of all weighted components
                decimal totalScore = 
                    academicScore + 
                    extracurricularScore + 
                    seminarScore + 
                    masteryScore + 
                    challengesScore;
                
                _logger.LogInformation($"Updated component scores for student {studentId}: " +
                    $"Academic={academicScore} (already weighted), " +
                    $"Extracurricular={extracurricularScore}, " +
                    $"Seminars={seminarScore} (already weighted), " +
                    $"Mastery={masteryScore}, " +
                    $"Challenges={challengesScore}");
                
                _logger.LogInformation($"Overall score calculation with correct formula: " +
                    $"OverallScore = AcademicScore + ExtracurricularScore + SeminarsScore + MasteryScore + ChallengesScore = " +
                    $"{academicScore} + {extracurricularScore} + {seminarScore} + {masteryScore} + {challengesScore} = {totalScore}");
                
                // Update the overall score in the database
                string updateQuery = @"
                    UPDATE StudentDetails 
                    SET Score = @Score
                    WHERE UserId = @StudentId OR IdNumber = @StudentId";
                
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Score", totalScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        _logger.LogInformation($"Updated overall score for student with {(studentId.Contains("-") ? "UserId" : "IdNumber")} {studentId} to {totalScore}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to update overall score for student {studentId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RecalculateScore for student {StudentId}", studentId);
                throw;
            }
        }
        
        // Helper method to get the current score weights from the database
        private async Task<Dictionary<string, decimal>> GetScoreWeights(SqlConnection connection)
        {
            Dictionary<string, decimal> weights = new Dictionary<string, decimal>
            {
                { "AcademicGrades", 30.0m },
                { "CompletedChallenges", 20.0m },
                { "Mastery", 20.0m },
                { "SeminarsWebinars", 10.0m },
                { "Extracurricular", 20.0m }
            };

            try
            {
                // Check if ScoreWeights table exists
                bool tableExists = await TableExists(connection, "ScoreWeights");
                if (!tableExists)
                {
                    _logger.LogWarning("ScoreWeights table doesn't exist. Using default weights.");
                    return weights;
                }

                // Query weights from the database
                string query = "SELECT CategoryName, Weight FROM ScoreWeights";
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string categoryName = reader.GetString(0);
                            decimal weight = reader.GetDecimal(1);
                            
                            // Update the dictionary with the actual weight from the database
                            if (weights.ContainsKey(categoryName))
                            {
                                weights[categoryName] = weight;
                            }
                        }
                    }
                }

                // Validate that weights add up to 100%
                decimal totalWeight = weights.Values.Sum();
                if (Math.Abs(totalWeight - 100) > 0.1m)
                {
                    _logger.LogWarning($"Score weights do not add up to 100%. Current total: {totalWeight}%. Normalizing weights.");
                    
                    // Normalize weights to ensure they add up to 100%
                    foreach (var key in weights.Keys.ToList())
                    {
                        weights[key] = (weights[key] / totalWeight) * 100.0m;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving score weights. Using default weights.");
            }

            return weights;
        }

        // Ensure all required score columns exist in the database
        private async Task EnsureScoreColumnsExist(SqlConnection connection)
        {
            try
            {
                // Check and create AcademicGradesScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "AcademicGradesScore"))
                {
                    _logger.LogWarning("AcademicGradesScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD AcademicGradesScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created AcademicGradesScore column with default value 0");
                    }
                }
                
                // Check and create ExtracurricularScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "ExtracurricularScore"))
                {
                    _logger.LogWarning("ExtracurricularScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD ExtracurricularScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created ExtracurricularScore column with default value 0");
                    }
                }
                
                // Check and create SeminarsWebinarsScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "SeminarsWebinarsScore"))
                {
                    _logger.LogWarning("SeminarsWebinarsScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD SeminarsWebinarsScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created SeminarsWebinarsScore column with default value 0");
                    }
                }
                
                // Check and create MasteryScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "MasteryScore"))
                {
                    _logger.LogWarning("MasteryScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD MasteryScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created MasteryScore column with default value 0");
                    }
                }
                
                // Check and create CompletedChallengesScore if needed
                if (!await ColumnExists(connection, "StudentDetails", "CompletedChallengesScore"))
                {
                    _logger.LogWarning("CompletedChallengesScore column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD CompletedChallengesScore decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created CompletedChallengesScore column with default value 0");
                    }
                }
                
                // Check and create Score column if needed
                if (!await ColumnExists(connection, "StudentDetails", "Score"))
                {
                    _logger.LogWarning("Score column does not exist. Creating it...");
                    using (var command = new SqlCommand(
                        "ALTER TABLE StudentDetails ADD Score decimal(5,2) DEFAULT 0", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Created Score column");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring score columns exist");
                // Continue execution despite errors to allow partial functionality
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

        // Helper method to check if a student exists
        private async Task<bool> StudentExists(SqlConnection connection, string studentId)
        {
            string query = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @StudentId";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                int count = (int)await command.ExecuteScalarAsync();
                return count > 0;
            }
        }

        // Helper method to get row count with a specific value
        private async Task<int> GetRowCount(SqlConnection connection, string tableName, string columnName, string value)
        {
            string query = $"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = @Value";
            
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Value", value);
                return (int)await command.ExecuteScalarAsync();
            }
        }

        [HttpGet]
        [Route("GetStudentScoreBreakdown")]
        public async Task<IActionResult> GetStudentScoreBreakdown(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for GetStudentScoreBreakdown");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Getting score breakdown for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Check if student exists
                    if (!await StudentExists(connection, studentId))
                    {
                        _logger.LogWarning($"Student {studentId} not found in database");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Get the student's scores from StudentDetails
                    decimal academicScore = 0;
                    decimal extracurricularScore = 0;
                    decimal seminarScore = 0;
                    decimal masteryScore = 0;
                    decimal challengesScore = 0;
                    decimal overallScore = 0;
                    
                    string query = @"
                        SELECT 
                            COALESCE(AcademicGradesScore, 0) as AcademicGradesScore,
                            COALESCE(ExtracurricularScore, 0) as ExtracurricularScore, 
                            COALESCE(SeminarsWebinarsScore, 0) as SeminarsWebinarsScore,
                            COALESCE(MasteryScore, 0) as MasteryScore,
                            COALESCE(CompletedChallengesScore, 0) as CompletedChallengesScore,
                            COALESCE(Score, 0) as Score
                        FROM StudentDetails 
                        WHERE UserId = @StudentId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Check if all the required columns exist
                                bool hasAcademicScore = reader.GetOrdinal("AcademicGradesScore") >= 0;
                                bool hasExtracurricularScore = reader.GetOrdinal("ExtracurricularScore") >= 0;
                                bool hasSeminarScore = reader.GetOrdinal("SeminarsWebinarsScore") >= 0;
                                bool hasMasteryScore = reader.GetOrdinal("MasteryScore") >= 0;
                                bool hasChallengesScore = reader.GetOrdinal("CompletedChallengesScore") >= 0;
                                bool hasOverallScore = reader.GetOrdinal("Score") >= 0;
                                
                                if (hasAcademicScore) academicScore = reader.GetDecimal(reader.GetOrdinal("AcademicGradesScore"));
                                if (hasExtracurricularScore) extracurricularScore = reader.GetDecimal(reader.GetOrdinal("ExtracurricularScore"));
                                if (hasSeminarScore) seminarScore = reader.GetDecimal(reader.GetOrdinal("SeminarsWebinarsScore"));
                                if (hasMasteryScore) masteryScore = reader.GetDecimal(reader.GetOrdinal("MasteryScore"));
                                if (hasChallengesScore) challengesScore = reader.GetDecimal(reader.GetOrdinal("CompletedChallengesScore"));
                                if (hasOverallScore) overallScore = reader.GetDecimal(reader.GetOrdinal("Score"));
                            }
                        }
                    }
                    
                    // Get dynamic weights from the ScoreWeights table
                    Dictionary<string, decimal> weights = await GetScoreWeights(connection);
                    
                    // Calculate the weighted contribution of each component to the overall score using dynamic weights
                    decimal academicWeighted = academicScore * (weights["AcademicGrades"] / 100.0m);
                    decimal extracurricularWeighted = extracurricularScore * (weights["Extracurricular"] / 100.0m);
                    decimal seminarWeighted = seminarScore * (weights["SeminarsWebinars"] / 100.0m);
                    decimal masteryWeighted = masteryScore * (weights["Mastery"] / 100.0m);
                    decimal challengesWeighted = challengesScore * (weights["CompletedChallenges"] / 100.0m);
                    
                    return Json(new { 
                        success = true,
                        studentId = studentId,
                        scores = new {
                            academic = new {
                                raw = academicScore,
                                weighted = academicWeighted,
                                percentage = academicScore, // Raw score is already a percentage (0-100)
                                weight = weights["AcademicGrades"]
                            },
                            extracurricular = new {
                                raw = extracurricularScore,
                                weighted = extracurricularWeighted,
                                percentage = extracurricularScore,
                                weight = weights["Extracurricular"]
                            },
                            seminars = new {
                                raw = seminarScore,
                                weighted = seminarWeighted,
                                percentage = seminarScore,
                                weight = weights["SeminarsWebinars"]
                            },
                            mastery = new {
                                raw = masteryScore,
                                weighted = masteryWeighted,
                                percentage = masteryScore,
                                weight = weights["Mastery"]
                            },
                            challenges = new {
                                raw = challengesScore,
                                weighted = challengesWeighted,
                                percentage = challengesScore,
                                weight = weights["CompletedChallenges"]
                            }
                        },
                        overall = new {
                            score = overallScore,
                            percentage = overallScore
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting score breakdown for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateSeminarsScore")]
        public async Task<IActionResult> UpdateSeminarsScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateSeminarsScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating seminar score for student {studentId}");
                
                // Just call our existing method to calculate seminar score from attendance
                return await CalculateSeminarsScoreFromAttendance(studentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating seminar score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateExtracurricularScore")]
        public async Task<IActionResult> UpdateExtracurricularScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for UpdateExtracurricularScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Updating extracurricular score for student {studentId}");
                
                // Call our existing method to calculate extracurricular score from activities
                return await CalculateExtracurricularScoreFromActivities(studentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating extracurricular score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to update the seminar score value with proper error handling
        private async Task<bool> UpdateSeminarScoreValue(SqlConnection connection, string studentId, decimal seminarScore)
        {
            try
            {
                bool updated = false;
                
                // First try to update by UserId
                string updateQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE UserId = @StudentId";
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated SeminarsWebinarsScore to {seminarScore} for student with UserId {studentId}");
                    }
                }
                
                // If no rows were affected, try using IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET SeminarsWebinarsScore = @SeminarScore WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SeminarScore", seminarScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
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
                
                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating SeminarsWebinarsScore for student {studentId}");
                return false;
            }
        }

        // Helper method to update the extracurricular score value with proper error handling
        private async Task<bool> UpdateExtracurricularScoreValue(SqlConnection connection, string studentId, decimal extracurricularScore)
        {
            try
            {
                bool updated = false;
                
                // First try to update by UserId
                string updateQuery = "UPDATE StudentDetails SET ExtracurricularScore = @ExtracurricularScore WHERE UserId = @StudentId";
                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@ExtracurricularScore", extracurricularScore);
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    
                    if (rowsAffected > 0)
                    {
                        updated = true;
                        _logger.LogInformation($"Updated ExtracurricularScore to {extracurricularScore} for student with UserId {studentId}");
                    }
                }
                
                // If no rows were affected, try using IdNumber
                if (!updated)
                {
                    string updateByIdQuery = "UPDATE StudentDetails SET ExtracurricularScore = @ExtracurricularScore WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateByIdQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ExtracurricularScore", extracurricularScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            updated = true;
                            _logger.LogInformation($"Updated ExtracurricularScore to {extracurricularScore} for student with IdNumber {studentId}");
                        }
                    }
                }
                
                if (!updated)
                {
                    _logger.LogWarning($"Failed to update ExtracurricularScore for student {studentId}. No matching record found.");
                }
                
                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ExtracurricularScore for student {studentId}");
                return false;
            }
        }

        [HttpPost]
        [Route("CalculateMasteryScore")]
        public async Task<IActionResult> CalculateMasteryScore(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    _logger.LogError("Student ID is required for CalculateMasteryScore");
                    return Json(new { success = false, message = "Student ID is required." });
                }

                _logger.LogInformation($"Calculating mastery score for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        // Try with the resolved UserId as a last resort
                        string checkResolvedIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(checkResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int count = (int)await command.ExecuteScalarAsync();
                            if (count > 0)
                            {
                                studentExists = true;
                                studentId = resolvedUserId; // Use resolved ID for subsequent queries
                                _logger.LogInformation($"Using resolved UserId {resolvedUserId} for mastery score calculations");
                            }
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Get the Mastery score weight
                    var weights = await GetScoreWeights(connection);
                    decimal masteryWeight = weights["Mastery"];
                    _logger.LogInformation($"Using mastery weight: {masteryWeight}%");

                    // Define the languages we're tracking
                    string[] languages = { "HTML", "CSS", "JavaScript", "Python", "CSharp", "Java", "SQL", "PHP" };
                    
                    // Scores for each language
                    Dictionary<string, decimal> languageScores = new Dictionary<string, decimal>();
                    foreach (var language in languages)
                    {
                        languageScores[language] = 0;
                    }

                    // Calculate scores for each language
                    foreach (var language in languages)
                    {
                        // Count tests taken for this language
                        int testsTaken = await CountTestsTakenForLanguage(connection, studentId, language);
                        
                        // Calculate multiplier based on tests taken
                        decimal multiplier = 0.25m; // Default is 25% for 1 test
                        if (testsTaken >= 4) multiplier = 1.0m;      // 100% for 4+ tests
                        else if (testsTaken == 3) multiplier = 0.75m; // 75% for 3 tests
                        else if (testsTaken == 2) multiplier = 0.5m;  // 50% for 2 tests
                        else if (testsTaken <= 0) multiplier = 0;     // 0% if no tests taken
                        
                        // Get the raw score for this language (points earned / total possible)
                        decimal earnedPoints = 0;
                        decimal totalPoints = 0;
                        
                        string pointsQuery = @"
                            SELECT 
                                SUM(Points) AS EarnedPoints,
                                SUM(TotalPoints) AS TotalPoints
                            FROM TestAnswers
                            JOIN ProgrammingTests ON TestAnswers.TestId = ProgrammingTests.TestId
                            WHERE StudentId = @StudentId AND ProgrammingLanguage = @Language";
                        
                        using (var command = new SqlCommand(pointsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            command.Parameters.AddWithValue("@Language", language);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    if (!reader.IsDBNull(0)) earnedPoints = Convert.ToDecimal(reader[0]);
                                    if (!reader.IsDBNull(1)) totalPoints = Convert.ToDecimal(reader[1]);
                                }
                            }
                        }
                        
                        // Calculate language score with multiplier
                        decimal languageScore = 0;
                        if (totalPoints > 0)
                        {
                            // Raw score * multiplier
                            languageScore = (earnedPoints / totalPoints) * 100 * multiplier;
                        }
                        
                        // Store the language score
                        languageScores[language] = languageScore;
                        
                        // Update the individual language score in the database
                        string updateLangScoreQuery = $"UPDATE StudentDetails SET {language}Score = @Score WHERE ";
                        
                        if (!string.IsNullOrEmpty(resolvedUserId))
                        {
                            updateLangScoreQuery += "UserId = @StudentId";
                        }
                        else
                        {
                            updateLangScoreQuery += "IdNumber = @StudentId";
                        }
                        
                        using (var command = new SqlCommand(updateLangScoreQuery, connection))
                        {
                            command.Parameters.AddWithValue("@Score", languageScore);
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            await command.ExecuteNonQueryAsync();
                        }
                        
                        _logger.LogInformation($"Language: {language}, Tests taken: {testsTaken}, " +
                                             $"Multiplier: {multiplier}, Raw score: {(totalPoints > 0 ? (earnedPoints / totalPoints) * 100 : 0)}, " +
                                             $"Final score: {languageScore}");
                    }
                    
                    // Calculate the overall mastery score
                    decimal totalLanguageScore = 0;
                    foreach (var score in languageScores.Values)
                    {
                        totalLanguageScore += score;
                    }
                    
                    // Divide by number of languages and apply weight
                    decimal numLanguages = languages.Length;
                    decimal averageLanguageScore = totalLanguageScore / numLanguages;
                    decimal masteryScore = averageLanguageScore * (masteryWeight / 100.0m);
                    
                    _logger.LogInformation($"Total language score: {totalLanguageScore}, " +
                                         $"Average (/{numLanguages}): {averageLanguageScore}, " +
                                         $"Final mastery score with weight ({masteryWeight}%): {masteryScore}");
                    
                    // Update the mastery score in the database
                    string updateQuery = "UPDATE StudentDetails SET MasteryScore = @MasteryScore WHERE ";
                    
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        updateQuery += "UserId = @StudentId";
                    }
                    else
                    {
                        updateQuery += "IdNumber = @StudentId";
                    }
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@MasteryScore", masteryScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Updated MasteryScore for student {studentId} to {masteryScore}");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to update MasteryScore for student {studentId}");
                        }
                    }

                    // Recalculate the overall score
                    await RecalculateScore(connection, studentId);

                    return Json(new { 
                        success = true,
                        score = masteryScore,
                        languageScores = languageScores,
                        message = $"Updated mastery score to {masteryScore}" 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating mastery score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        // Helper method to count the number of tests taken for a specific language
        private async Task<int> CountTestsTakenForLanguage(SqlConnection connection, string studentId, string language)
        {
            string query = @"
                SELECT COUNT(DISTINCT ts.TestId)
                FROM TestAnswers ta
                JOIN TestSubmissions ts ON ta.SubmissionId = ts.SubmissionId
                JOIN ProgrammingTests pt ON ts.TestId = pt.TestId
                WHERE ts.StudentId = @StudentId AND pt.ProgrammingLanguage = @Language";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@StudentId", studentId);
                command.Parameters.AddWithValue("@Language", language);
                
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
            }
            
            return 0;
        }

        [HttpPost]
        [Route("CalculateCompletedChallengesScore")]
        public async Task<IActionResult> CalculateCompletedChallengesScore(string studentId)
        {
            try
            {
                bool processAllStudents = string.IsNullOrEmpty(studentId);
                
                if (processAllStudents)
                {
                    _logger.LogInformation("Calculating completed challenges scores for ALL students");
                }

                _logger.LogInformation($"Calculating completed challenges score for student {studentId}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Process all students if studentId is null
                    if (processAllStudents)
                    {
                        // Get all student IDs
                        List<string> studentIds = new List<string>();
                        string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                        
                        using (var command = new SqlCommand(getStudentsQuery, connection))
                        {
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    string id = reader.GetString(0);
                                    studentIds.Add(id);
                                }
                            }
                        }
                        
                        _logger.LogInformation($"Found {studentIds.Count} students to update challenge scores");
                        
                        // Get the current and previous weights
                        var weights = await GetScoreWeights(connection);
                        decimal newChallengesWeight = weights["CompletedChallenges"];
                        decimal previousChallengesWeight = 0;
                        
                        // Try to find the old weight from the database
                        string oldWeightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                        using (var command = new SqlCommand(oldWeightQuery, connection))
                        {
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                previousChallengesWeight = Convert.ToDecimal(result);
                            }
                        }
                        
                        // If we couldn't find the old weight, use the current weight
                        if (previousChallengesWeight <= 0)
                        {
                            previousChallengesWeight = newChallengesWeight;
                            _logger.LogWarning($"Could not determine previous challenges weight, using current weight: {previousChallengesWeight}%");
                        }
                        
                        _logger.LogInformation($"Using weights for adjustment: old={previousChallengesWeight}%, new={newChallengesWeight}%");
                        
                        int successCount = 0;
                        int skipCount = 0;
                        int failCount = 0;
                        
                        // Process each student
                        foreach (var id in studentIds)
                        {
                            try
                            {
                                // Get current challenges score
                                decimal currentChallengeScore = 0;
                                string scoreQuery = "SELECT CompletedChallengesScore FROM StudentDetails WHERE IdNumber = @StudentId";
                                using (var command = new SqlCommand(scoreQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@StudentId", id);
                                    var result = await command.ExecuteScalarAsync();
                                    if (result != null && result != DBNull.Value)
                                    {
                                        currentChallengeScore = Convert.ToDecimal(result);
                                    }
                                }
                                
                                // Skip if score is zero
                                if (currentChallengeScore <= 0)
                                {
                                    _logger.LogInformation($"Skipping student {id} with zero challenges score");
                                    skipCount++;
                                    continue;
                                }
                                
                                decimal adjustedChallengeScore = currentChallengeScore;
                                
                                // Apply the weight adjustment formula if weights differ
                                if (Math.Abs(previousChallengesWeight - newChallengesWeight) > 0.01m)
                                {
                                    adjustedChallengeScore = currentChallengeScore * (newChallengesWeight / previousChallengesWeight);
                                    _logger.LogInformation($"Adjusted score for student {id}: {currentChallengeScore} * ({newChallengesWeight}/{previousChallengesWeight}) = {adjustedChallengeScore}");
                                }
                                else
                                {
                                    _logger.LogInformation($"No adjustment needed for student {id}, keeping score: {currentChallengeScore}");
                                }
                                
                                // Update the CompletedChallengesScore in StudentDetails
                                string studentUpdateQuery = "UPDATE StudentDetails SET CompletedChallengesScore = @ChallengesScore WHERE IdNumber = @StudentId";
                                using (var command = new SqlCommand(studentUpdateQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@ChallengesScore", adjustedChallengeScore);
                                    command.Parameters.AddWithValue("@StudentId", id);
                                    int rowsAffected = await command.ExecuteNonQueryAsync();
                                    
                                    if (rowsAffected > 0)
                                    {
                                        _logger.LogInformation($"Updated CompletedChallengesScore for student {id} to {adjustedChallengeScore}");
                                        successCount++;
                                        
                                        
                                        // Recalculate the overall score
                                        await RecalculateScore(connection, id);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Failed to update challenge score for student {id}");
                                        failCount++;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error updating challenge score for student {id}");
                                failCount++;
                            }
                        }
                        
                        return Json(new { 
                            success = true,
                            updated = successCount,
                            skipped = skipCount,
                            failed = failCount,
                            message = $"Updated challenge scores for {successCount} students, skipped {skipCount}. Failed for {failCount} students." 
                        });
                    }
                    
                    // Process a single student
                    // Try to resolve UserId from StudentId if needed
                    string resolvedUserId = await ResolveUserIdFromStudentId(connection, studentId);
                    if (!string.IsNullOrEmpty(resolvedUserId))
                    {
                        _logger.LogInformation($"Resolved UserId {resolvedUserId} from StudentId {studentId}");
                    }

                    // Check if student exists with both UserId and IdNumber
                    bool studentExists = await StudentExists(connection, studentId);
                    
                    if (!studentExists)
                    {
                        // Try with IdNumber if not found with UserId
                        string checkIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE IdNumber = @StudentId";
                        using (var command = new SqlCommand(checkIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            int count = (int)await command.ExecuteScalarAsync();
                            studentExists = count > 0;
                        }
                    }
                    
                    if (!studentExists && !string.IsNullOrEmpty(resolvedUserId))
                    {
                        // Try with the resolved UserId as a last resort
                        string checkResolvedIdQuery = "SELECT COUNT(*) FROM StudentDetails WHERE UserId = @ResolvedId";
                        using (var command = new SqlCommand(checkResolvedIdQuery, connection))
                        {
                            command.Parameters.AddWithValue("@ResolvedId", resolvedUserId);
                            int count = (int)await command.ExecuteScalarAsync();
                            if (count > 0)
                            {
                                studentExists = true;
                                studentId = resolvedUserId; // Use resolved ID for subsequent queries
                                _logger.LogInformation($"Using resolved UserId {resolvedUserId} for challenges score calculations");
                            }
                        }
                    }
                    
                    if (!studentExists)
                    {
                        _logger.LogWarning($"Student {studentId} not found in database (checked both UserId and IdNumber)");
                        return Json(new { success = false, message = "Student not found." });
                    }

                    // Ensure the StudentDetails table has a CompletedChallengesScore column
                    if (!await ColumnExists(connection, "StudentDetails", "CompletedChallengesScore"))
                    {
                        _logger.LogWarning("CompletedChallengesScore column does not exist in StudentDetails table. Creating it...");
                        using (var command = new SqlCommand(
                            "ALTER TABLE StudentDetails ADD CompletedChallengesScore decimal(5,2) DEFAULT 0", connection))
                        {
                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation("Created CompletedChallengesScore column with default value 0");
                        }
                    }

                    // Get existing challenges score before calculating new one
                    decimal existingChallengesScore = 0;
                    string getExistingScoreQuery = @"
                        SELECT COALESCE(CompletedChallengesScore, 0) 
                        FROM StudentDetails 
                        WHERE UserId = @StudentId OR IdNumber = @StudentId";
                        
                    using (var command = new SqlCommand(getExistingScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            existingChallengesScore = Convert.ToDecimal(result);
                            _logger.LogInformation($"Existing challenges score for student {studentId}: {existingChallengesScore}");
                        }
                    }

                    // Get the current and previous weight
                    var currentWeights = await GetScoreWeights(connection);
                    decimal currentChallengesWeight = currentWeights["CompletedChallenges"];
                    decimal oldChallengesWeight = 0;
                    
                    // Try to find the old weight from the database
                    string getOldWeightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                    using (var command = new SqlCommand(getOldWeightQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            oldChallengesWeight = Convert.ToDecimal(result);
                        }
                    }
                    
                    // If we couldn't find the old weight, use the current weight
                    if (oldChallengesWeight <= 0)
                    {
                        oldChallengesWeight = currentChallengesWeight;
                        _logger.LogWarning($"Could not determine previous challenges weight, using current weight: {oldChallengesWeight}%");
                    }
                    
                    // Use weight adjustment formula for existing scores
                    decimal challengesScore = existingChallengesScore;
                    
                    // Skip if score is zero
                    if (existingChallengesScore > 0 && Math.Abs(oldChallengesWeight - currentChallengesWeight) > 0.01m)
                    {
                        // Apply the weight adjustment formula
                        challengesScore = existingChallengesScore * (currentChallengesWeight / oldChallengesWeight);
                        _logger.LogInformation($"Adjusted challenges score using formula: {existingChallengesScore} * ({currentChallengesWeight}/{oldChallengesWeight}) = {challengesScore}");
                    }
                    else
                    {
                        // No adjustment needed, keep existing score
                        _logger.LogInformation($"No adjustment needed for student {studentId}, keeping existing score: {existingChallengesScore}");
                    }
                    
                    // Update the CompletedChallengesScore in StudentDetails
                    string updateQuery = "UPDATE StudentDetails SET CompletedChallengesScore = @ChallengesScore WHERE UserId = @StudentId OR IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ChallengesScore", challengesScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            _logger.LogInformation($"Updated CompletedChallengesScore for student {studentId} to {challengesScore}");
                            
                            // Recalculate the overall score
                            await RecalculateScore(connection, studentId);
                            
                            return Json(new { 
                                success = true,
                                score = challengesScore,
                                message = $"Completed challenges score updated to {challengesScore}" 
                            });
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to update CompletedChallengesScore for student {studentId}");
                            return Json(new { success = false, message = "Failed to update challenges score" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating completed challenges score for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("RecalculateAllMasteryScoresWithNewWeights")]
        public async Task<IActionResult> RecalculateAllMasteryScoresWithNewWeights()
        {
            try
            {
                _logger.LogInformation("Recalculating all mastery scores with new weights");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the current score weight
                    var weights = await GetScoreWeights(connection);
                    decimal masteryWeight = weights["Mastery"];
                    
                    _logger.LogInformation($"Using mastery weight: {masteryWeight}%");
                    
                    // Get all student IDs
                    List<string> studentIds = new List<string>();
                    string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string studentId = reader.GetString(0);
                                studentIds.Add(studentId);
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {studentIds.Count} students to update");
                    
                    int successCount = 0;
                    int failCount = 0;
                    
                    // Process each student
                    foreach (var studentId in studentIds)
                    {
                        try
                        {
                            // Calculate the mastery score with the new weight
                            decimal newMasteryScore = await RecalculateMasteryScore(connection, studentId, masteryWeight);
                            
                            // Update the database with the new score
                            bool updated = false;
                            
                            // First try with ID number
                            string updateByIdQuery = "UPDATE StudentDetails SET MasteryScore = @MasteryScore WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(updateByIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@MasteryScore", newMasteryScore);
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                int rowsAffected = await command.ExecuteNonQueryAsync();
                                
                                if (rowsAffected > 0)
                                {
                                    updated = true;
                                }
                            }
                            
                            if (updated)
                            {
                                _logger.LogInformation($"Updated MasteryScore for student {studentId} to {newMasteryScore}");
                                successCount++;
                                
                                // Recalculate the overall score
                                await RecalculateScore(connection, studentId);
                            }
                            else
                            {
                                _logger.LogWarning($"Failed to update MasteryScore for student {studentId}");
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating mastery score for student {studentId}");
                            failCount++;
                        }
                    }
                    
                    return Json(new { 
                        success = true,
                        updated = successCount,
                        failed = failCount,
                        message = $"Updated mastery scores for {successCount} students with new weight {masteryWeight}%. Failed for {failCount} students." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating all mastery scores");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Add this new method that recalculates challenges score from raw data
        private async Task<decimal> RecalculateChallengesScore(SqlConnection connection, string studentId, decimal scoreWeight)
        {
            _logger.LogInformation($"Recalculating challenges score with new weight {scoreWeight} for student {studentId}");
            
            // Get existing challenges score first
            decimal existingChallengesScore = 0;
            try
            {
                string getScoreQuery = "SELECT COALESCE(CompletedChallengesScore, 0) FROM StudentDetails WHERE UserId = @StudentId OR IdNumber = @StudentId";
                using (var command = new SqlCommand(getScoreQuery, connection))
                {
                    command.Parameters.AddWithValue("@StudentId", studentId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        existingChallengesScore = Convert.ToDecimal(result);
                        _logger.LogInformation($"Found existing challenges score for student {studentId}: {existingChallengesScore}");
                    }
                }

                // If existing score is 0, return 0 - no weight adjustment needed
                if (existingChallengesScore <= 0)
                {
                    _logger.LogInformation($"Existing score is 0 for student {studentId}, no weight adjustment needed");
                    return 0;
                }

                // Get old weight
                decimal oldWeight = 0;
                string getOldWeightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                using (var weightCommand = new SqlCommand(getOldWeightQuery, connection))
                {
                    var weightResult = await weightCommand.ExecuteScalarAsync();
                    if (weightResult != null && weightResult != DBNull.Value)
                    {
                        oldWeight = Convert.ToDecimal(weightResult);
                        _logger.LogInformation($"Found old weight from database: {oldWeight}");
                    }
                }
                
                // If old weight couldn't be determined, use the scoreWeight parameter as the old weight
                if (oldWeight <= 0)
                {
                    _logger.LogWarning($"Couldn't determine old weight, using current weight {scoreWeight} as reference");
                    oldWeight = scoreWeight;
                }
                
                // Always apply adjustment formula - ensures consistency with how score weights are applied
                decimal adjustedScore = existingChallengesScore * (scoreWeight / oldWeight);
                _logger.LogInformation($"Applied weight adjustment formula: {existingChallengesScore} * ({scoreWeight}/{oldWeight}) = {adjustedScore}");
                return adjustedScore;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adjusting challenge score for student {studentId}");
                // Return existing score even on error to avoid losing data
                return existingChallengesScore;
            }
        }

        [HttpPost]
        [Route("RecalculateAllScoresWithNewWeights")]
        public async Task<IActionResult> RecalculateAllScoresWithNewWeights()
        {
            try
            {
                _logger.LogInformation("Recalculating all scores with new weights");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the current score weights
                    var updatedWeights = await GetScoreWeights(connection);
                    decimal updatedMasteryWeight = updatedWeights["Mastery"];
                    decimal updatedChallengesWeight = updatedWeights["CompletedChallenges"];
                    decimal updatedExtracurricularWeight = updatedWeights["Extracurricular"];
                    
                    _logger.LogInformation($"Using weights - Mastery: {updatedMasteryWeight}%, Challenges: {updatedChallengesWeight}%, Extracurricular: {updatedExtracurricularWeight}%");
                    
                    // Get all student IDs
                    List<string> studentIds = new List<string>();
                    string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string studentId = reader.GetString(0);
                                studentIds.Add(studentId);
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {studentIds.Count} students to update");
                    
                    int successCount = 0;
                    int failCount = 0;
                    
                    // Process each student
                    foreach (var studentId in studentIds)
                    {
                        try
                        {
                            // Calculate the scores with the new weights
                            decimal newMasteryScore = await RecalculateMasteryScore(connection, studentId, updatedMasteryWeight);
                            
                            // For challenges, directly apply the weight adjustment formula instead of recalculating
                            decimal currentChallengesScore = 0;
                            decimal newChallengesScore = 0;
                            decimal oldChallengesWeight = 0;
                            
                            // Get current challenges score
                            string getChallengesScoreQuery = "SELECT CompletedChallengesScore FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(getChallengesScoreQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    currentChallengesScore = Convert.ToDecimal(result);
                                }
                            }
                            
                            // Get old challenge weight
                            string getOldWeightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                            using (var command = new SqlCommand(getOldWeightQuery, connection))
                            {
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    oldChallengesWeight = Convert.ToDecimal(result);
                                }
                            }
                            
                            // If we have an existing score, adjust it based on weight change
                            if (currentChallengesScore > 0 && oldChallengesWeight > 0 && 
                                Math.Abs(oldChallengesWeight - updatedChallengesWeight) > 0.01m)
                            {
                                // Use weight adjustment formula
                                newChallengesScore = currentChallengesScore * (updatedChallengesWeight / oldChallengesWeight);
                                _logger.LogInformation($"Adjusted challenges score for student {studentId}: {currentChallengesScore} * ({updatedChallengesWeight}/{oldChallengesWeight}) = {newChallengesScore}");
                            }
                            else
                            {
                                // Keep existing score if no adjustment needed
                                newChallengesScore = currentChallengesScore;
                                _logger.LogInformation($"Using existing challenges score for student {studentId}: {currentChallengesScore}");
                            }
                            
                            // Get current extracurricular score and apply weight adjustment
                            decimal currentExtracurricularScore = 0;
                            decimal newExtracurricularScore = 0;
                            
                            string getScoreQuery = "SELECT ExtracurricularScore FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(getScoreQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    currentExtracurricularScore = Convert.ToDecimal(result);
                                    
                                    // Get old weight for comparison
                                    decimal oldExtracurricularWeight = 20m; // Default value
                                    if (updatedExtracurricularWeight != oldExtracurricularWeight && currentExtracurricularScore > 0)
                                    {
                                        newExtracurricularScore = currentExtracurricularScore * (updatedExtracurricularWeight / oldExtracurricularWeight);
                                        _logger.LogInformation($"Adjusted extracurricular score for student {studentId}: {currentExtracurricularScore} * ({updatedExtracurricularWeight}/{oldExtracurricularWeight}) = {newExtracurricularScore}");
                                    }
                                    else
                                    {
                                        newExtracurricularScore = currentExtracurricularScore;
                                    }
                                }
                                else
                                {
                                    // Call existing method to calculate if no score found
                                    await CalculateExtracurricularScoreFromActivities(studentId);
                                    
                                    // Get the newly calculated score
                                    using (var cmd = new SqlCommand(getScoreQuery, connection))
                                    {
                                        cmd.Parameters.AddWithValue("@StudentId", studentId);
                                        var newResult = await cmd.ExecuteScalarAsync();
                                        if (newResult != null && newResult != DBNull.Value)
                                        {
                                            newExtracurricularScore = Convert.ToDecimal(newResult);
                                        }
                                    }
                                }
                            }
                            
                            // Update the database with the new scores
                            bool updated = false;
                            
                            // Try with ID number
                            string updateByIdQuery = @"
                                UPDATE StudentDetails SET 
                                    MasteryScore = @MasteryScore,
                                    CompletedChallengesScore = @ChallengesScore,
                                    ExtracurricularScore = @ExtracurricularScore
                                WHERE IdNumber = @StudentId";
                                
                            using (var command = new SqlCommand(updateByIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@MasteryScore", newMasteryScore);
                                command.Parameters.AddWithValue("@ChallengesScore", newChallengesScore);
                                command.Parameters.AddWithValue("@ExtracurricularScore", newExtracurricularScore);
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                int rowsAffected = await command.ExecuteNonQueryAsync();
                                
                                if (rowsAffected > 0)
                                {
                                    updated = true;
                                }
                            }
                            
                            if (updated)
                            {
                                _logger.LogInformation($"Updated scores for student {studentId} - Mastery: {newMasteryScore}, Challenges: {newChallengesScore}, Extracurricular: {newExtracurricularScore}");
                                successCount++;
                                
                                // Recalculate the overall score
                                await RecalculateScore(connection, studentId);
                            }
                            else
                            {
                                _logger.LogWarning($"Failed to update scores for student {studentId}");
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating scores for student {studentId}");
                            failCount++;
                        }
                    }
                    
                    return Json(new { 
                        success = true,
                        updated = successCount,
                        failed = failCount,
                        message = $"Updated scores for {successCount} students with new weights: Mastery={updatedMasteryWeight}%, Challenges={updatedChallengesWeight}%, Extracurricular={updatedExtracurricularWeight}%. Failed for {failCount} students." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating scores with new weights");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Add this new method that recalculates mastery score from raw data
        private async Task<decimal> RecalculateMasteryScore(SqlConnection connection, string studentId, decimal scoreWeight)
        {
            _logger.LogInformation($"Recalculating mastery score with new weight {scoreWeight} for student {studentId}");
            
            try
            {
                // Define the languages we're tracking
                string[] languages = { "HTML", "CSS", "JavaScript", "Python", "CSharp", "Java", "SQL", "PHP" };
                
                // Scores for each language
                Dictionary<string, decimal> languageScores = new Dictionary<string, decimal>();
                foreach (var language in languages)
                {
                    languageScores[language] = 0;
                }

                // Calculate scores for each language
                foreach (var language in languages)
                {
                    // Count tests taken for this language
                    int testsTaken = await CountTestsTakenForLanguage(connection, studentId, language);
                    
                    // Calculate multiplier based on tests taken
                    decimal multiplier = 0.25m; // Default is 25% for 1 test
                    if (testsTaken >= 4) multiplier = 1.0m;      // 100% for 4+ tests
                    else if (testsTaken == 3) multiplier = 0.75m; // 75% for 3 tests
                    else if (testsTaken == 2) multiplier = 0.5m;  // 50% for 2 tests
                    else if (testsTaken <= 0) multiplier = 0;     // 0% if no tests taken
                    
                    // Get the raw score for this language (points earned / total possible)
                    decimal earnedPoints = 0;
                    decimal totalPoints = 0;
                    
                    string pointsQuery = @"
                        SELECT 
                            SUM(Points) AS EarnedPoints,
                            SUM(TotalPoints) AS TotalPoints
                        FROM TestAnswers
                        JOIN ProgrammingTests ON TestAnswers.TestId = ProgrammingTests.TestId
                        WHERE StudentId = @StudentId AND ProgrammingLanguage = @Language";
                    
                    using (var command = new SqlCommand(pointsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        command.Parameters.AddWithValue("@Language", language);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                if (!reader.IsDBNull(0)) earnedPoints = Convert.ToDecimal(reader[0]);
                                if (!reader.IsDBNull(1)) totalPoints = Convert.ToDecimal(reader[1]);
                            }
                        }
                    }
                    
                    // Calculate language score with multiplier
                    decimal languageScore = 0;
                    if (totalPoints > 0)
                    {
                        // Raw score * multiplier
                        languageScore = (earnedPoints / totalPoints) * 100 * multiplier;
                    }
                    
                    // Store the language score
                    languageScores[language] = languageScore;
                    
                    // Update the individual language score in the database
                    string updateLangScoreQuery = $"UPDATE StudentDetails SET {language}Score = @Score WHERE IdNumber = @StudentId";
                    using (var command = new SqlCommand(updateLangScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Score", languageScore);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    _logger.LogInformation($"Language: {language}, Tests taken: {testsTaken}, " +
                                         $"Multiplier: {multiplier}, Score: {languageScore}");
                }
                
                // Calculate the overall mastery score
                decimal totalLanguageScore = 0;
                foreach (var score in languageScores.Values)
                {
                    totalLanguageScore += score;
                }
                
                // Divide by number of languages and apply weight
                decimal numLanguages = languages.Length;
                decimal averageLanguageScore = totalLanguageScore / numLanguages;
                decimal masteryScore = averageLanguageScore * (scoreWeight / 100.0m);
                
                _logger.LogInformation($"Total language score: {totalLanguageScore}, " +
                                     $"Average (/{numLanguages}): {averageLanguageScore}, " +
                                     $"Final mastery score with weight ({scoreWeight}%): {masteryScore}");
                
                return masteryScore;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calculating mastery score for student {studentId}");
                return 0;
            }
        }

        [HttpPost]
        [Route("UpdateMasteryScoresWhenWeightChanges")]
        public async Task<IActionResult> UpdateMasteryScoresWhenWeightChanges(decimal oldWeight = 0)
        {
            try
            {
                _logger.LogInformation("Recalculating all mastery scores due to weight change");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the current score weight
                    var weights = await GetScoreWeights(connection);
                    decimal newWeight = weights["Mastery"];
                    
                    _logger.LogInformation($"Using updated mastery weight: {newWeight}%");
                    
                    // If old weight wasn't provided, we'll use the current weight as fallback below
                    if (oldWeight <= 0)
                    {
                        _logger.LogInformation("No old weight provided, will use current weight as fallback");
                    }
                    
                    // If we still don't have an old weight, assume it's the same as new (no change)
                    if (oldWeight <= 0)
                    {
                        oldWeight = newWeight;
                        _logger.LogWarning($"Could not determine previous mastery weight, using current weight: {oldWeight}%");
                    }
                    
                    // No need to store the weight in ApplicationSettings since it's passed as a parameter
                    _logger.LogInformation($"Using old weight: {oldWeight}%, new weight: {newWeight}% for mastery score calculation");
                    
                    // Get all student IDs
                    List<string> studentIds = new List<string>();
                    string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string studentId = reader.GetString(0);
                                studentIds.Add(studentId);
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {studentIds.Count} students to update");
                    
                    int successCount = 0;
                    int failCount = 0;
                    
                    // Process each student
                    foreach (var studentId in studentIds)
                    {
                        try
                        {
                            // Get the current mastery score for this student
                            decimal currentMasteryScore = 0;
                            string getScoreQuery = "SELECT MasteryScore FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(getScoreQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    currentMasteryScore = Convert.ToDecimal(result);
                                }
                            }
                            
                            decimal newMasteryScore;
                            
                            // Only use the formula if we have both weights and a current score
                            if (oldWeight > 0 && newWeight > 0 && currentMasteryScore > 0)
                            {
                                // Formula: new_mastery_score = current_mastery_score * (new_weight / old_weight)
                                newMasteryScore = currentMasteryScore * (newWeight / oldWeight);
                                _logger.LogInformation($"Using formula for student {studentId}: {currentMasteryScore} * ({newWeight}/{oldWeight}) = {newMasteryScore}");
                            }
                            else
                            {
                                // Fallback to recalculating if formula can't be used
                                newMasteryScore = await RecalculateMasteryScore(connection, studentId, newWeight);
                                _logger.LogInformation($"Recalculated from scratch for student {studentId}: {newMasteryScore}");
                            }
                            
                            // Update the database with the new score
                            bool updated = false;
                            
                            string updateByIdQuery = "UPDATE StudentDetails SET MasteryScore = @MasteryScore WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(updateByIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@MasteryScore", newMasteryScore);
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                int rowsAffected = await command.ExecuteNonQueryAsync();
                                
                                if (rowsAffected > 0)
                                {
                                    updated = true;
                                }
                            }
                            
                            if (updated)
                            {
                                _logger.LogInformation($"Updated MasteryScore for student {studentId} to {newMasteryScore} with new weight {newWeight}%");
                                successCount++;
                                
                                // Recalculate the overall score
                                await RecalculateScore(connection, studentId);
                            }
                            else
                            {
                                _logger.LogWarning($"Failed to update MasteryScore for student {studentId}");
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating mastery score for student {studentId}");
                            failCount++;
                        }
                    }
                    
                    return Json(new { 
                        success = true,
                        updated = successCount,
                        failed = failCount,
                        oldWeight = oldWeight,
                        newWeight = newWeight,
                        message = $"Updated mastery scores for {successCount} students. Old weight: {oldWeight}%, New weight: {newWeight}%. Failed for {failCount} students." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating all mastery scores");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("UpdateChallengesScoresWhenWeightChanges")]
        public async Task<IActionResult> UpdateChallengesScoresWhenWeightChanges(decimal oldWeight = 0)
        {
            try
            {
                _logger.LogInformation("Recalculating all challenges scores due to weight change");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the current score weight
                    var weights = await GetScoreWeights(connection);
                    decimal newWeight = weights["CompletedChallenges"];
                    
                    _logger.LogInformation($"Using updated challenges weight: {newWeight}%");
                    
                    // If old weight wasn't provided, we'll use the current weight as fallback
                    if (oldWeight <= 0)
                    {
                        _logger.LogInformation("No old weight provided, will use current weight as fallback");
                        string getOldWeightQuery = "SELECT Weight FROM ScoreWeights WHERE CategoryName = 'CompletedChallenges'";
                        using (var command = new SqlCommand(getOldWeightQuery, connection))
                        {
                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                oldWeight = Convert.ToDecimal(result);
                                _logger.LogInformation($"Retrieved old challenges weight: {oldWeight}%");
                            }
                        }
                    }
                    
                    // If we still don't have an old weight, assume it's the same as new (no change)
                    if (oldWeight <= 0)
                    {
                        oldWeight = newWeight;
                        _logger.LogWarning($"Could not determine previous challenges weight, using current weight: {oldWeight}%");
                    }
                    
                    // Log actual weights being used for the calculation
                    _logger.LogInformation($"IMPORTANT: Using old weight: {oldWeight}%, new weight: {newWeight}% for challenges score calculation");
                    if (Math.Abs(oldWeight - newWeight) <= 0.01m)
                    {
                        _logger.LogWarning("Old and new weights are nearly identical. This might cause minimal changes.");
                    }
                    
                    // Get all student IDs
                    List<string> studentIds = new List<string>();
                    string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string studentId = reader.GetString(0);
                                studentIds.Add(studentId);
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {studentIds.Count} students to update");
                    
                    int successCount = 0;
                    int skipCount = 0;
                    int failCount = 0;
                    
                    // Process each student
                    foreach (var studentId in studentIds)
                    {
                        try
                        {
                            // Get the current challenges score for this student
                            decimal currentChallengesScore = 0;
                            string getScoreQuery = "SELECT COALESCE(CompletedChallengesScore, 0) FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(getScoreQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    currentChallengesScore = Convert.ToDecimal(result);
                                }
                            }
                            
                            // Calculate the new score using the weight adjustment formula
                            decimal newChallengesScore = currentChallengesScore * (newWeight / oldWeight);
                            
                            // Only skip students with zero scores if the old and new weights match
                            if (currentChallengesScore <= 0 && Math.Abs(oldWeight - newWeight) <= 0.01m)
                            {
                                _logger.LogInformation($"Skipping student {studentId} with zero challenges score (no weight change)");
                                skipCount++;
                                continue;
                            }
                            
                            _logger.LogInformation($"Adjusting score for student {studentId}: {currentChallengesScore} * ({newWeight}/{oldWeight}) = {newChallengesScore}");
                            
                            // Always update to ensure the score is correctly set
                            string updateQuery = "UPDATE StudentDetails SET CompletedChallengesScore = @ChallengesScore WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(updateQuery, connection))
                            {
                                command.Parameters.AddWithValue("@ChallengesScore", newChallengesScore);
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                int rowsAffected = await command.ExecuteNonQueryAsync();
                                
                                if (rowsAffected > 0)
                                {
                                    _logger.LogInformation($"Success! Updated CompletedChallengesScore for student {studentId} to {newChallengesScore}");
                                    successCount++;
                                    
                                    // Recalculate the overall score
                                    await RecalculateScore(connection, studentId);
                                }
                                else
                                {
                                    // Try with UserId if IdNumber update failed
                                    string updateByUserIdQuery = "UPDATE StudentDetails SET CompletedChallengesScore = @ChallengesScore WHERE UserId = @StudentId";
                                    using (var userIdCommand = new SqlCommand(updateByUserIdQuery, connection))
                                    {
                                        userIdCommand.Parameters.AddWithValue("@ChallengesScore", newChallengesScore);
                                        userIdCommand.Parameters.AddWithValue("@StudentId", studentId);
                                        rowsAffected = await userIdCommand.ExecuteNonQueryAsync();
                                        
                                        if (rowsAffected > 0)
                                        {
                                            _logger.LogInformation($"Success! Updated CompletedChallengesScore by UserId for student {studentId} to {newChallengesScore}");
                                            successCount++;
                                            
                                            // Recalculate the overall score
                                            await RecalculateScore(connection, studentId);
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"Failed to update CompletedChallengesScore for student {studentId} (tried both IdNumber and UserId)");
                                            failCount++;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating challenges score for student {studentId}");
                            failCount++;
                        }
                    }
                    
                    return Json(new { 
                        success = true,
                        updated = successCount,
                        skipped = skipCount,
                        failed = failCount,
                        oldWeight = oldWeight,
                        newWeight = newWeight,
                        message = $"Updated challenges scores for {successCount} students, skipped {skipCount}. Old weight: {oldWeight}%, New weight: {newWeight}%. Failed for {failCount} students." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating all challenges scores");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpPost]
        [Route("UpdateExtracurricularScoresWhenWeightChanges")]
        public async Task<IActionResult> UpdateExtracurricularScoresWhenWeightChanges(decimal oldWeight = 0)
        {
            try
            {
                _logger.LogInformation("Recalculating all extracurricular scores due to weight change");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the current score weight
                    var weights = await GetScoreWeights(connection);
                    decimal newWeight = weights["Extracurricular"];
                    
                    _logger.LogInformation($"Using updated extracurricular weight: {newWeight}%");
                    
                    // If old weight wasn't provided, we'll use the current weight as fallback
                    if (oldWeight <= 0)
                    {
                        _logger.LogInformation("No old weight provided, will use current weight as fallback");
                    }
                    
                    // If we still don't have an old weight, assume it's the same as new (no change)
                    if (oldWeight <= 0)
                    {
                        oldWeight = newWeight;
                        _logger.LogWarning($"Could not determine previous extracurricular weight, using current weight: {oldWeight}%");
                    }
                    
                    _logger.LogInformation($"Using old weight: {oldWeight}%, new weight: {newWeight}% for extracurricular score calculation");
                    
                    // Get all student IDs
                    List<string> studentIds = new List<string>();
                    string getStudentsQuery = "SELECT DISTINCT IdNumber FROM StudentDetails WHERE IdNumber IS NOT NULL";
                    
                    using (var command = new SqlCommand(getStudentsQuery, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string studentId = reader.GetString(0);
                                studentIds.Add(studentId);
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Found {studentIds.Count} students to update");
                    
                    int successCount = 0;
                    int failCount = 0;
                    
                    // Process each student
                    foreach (var studentId in studentIds)
                    {
                        try
                        {
                            // Get the current extracurricular score for this student
                            decimal currentExtracurricularScore = 0;
                            string getScoreQuery = "SELECT ExtracurricularScore FROM StudentDetails WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(getScoreQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    currentExtracurricularScore = Convert.ToDecimal(result);
                                }
                            }
                            
                            decimal newExtracurricularScore;
                            
                            // Only use the formula if we have both weights and a current score
                            if (oldWeight > 0 && newWeight > 0 && currentExtracurricularScore > 0)
                            {
                                // Formula: new_extracurricular_score = current_extracurricular_score * (new_weight / old_weight)
                                newExtracurricularScore = currentExtracurricularScore * (newWeight / oldWeight);
                                _logger.LogInformation($"Using formula for student {studentId}: {currentExtracurricularScore} * ({newWeight}/{oldWeight}) = {newExtracurricularScore}");
                            }
                            else
                            {
                                // Fallback to recalculating from activities
                                // Call existing method to recalculate extracurricular score
                                await CalculateExtracurricularScoreFromActivities(studentId);
                                
                                // Get the updated score
                                using (var command = new SqlCommand(getScoreQuery, connection))
                                {
                                    command.Parameters.AddWithValue("@StudentId", studentId);
                                    var result = await command.ExecuteScalarAsync();
                                    if (result != null && result != DBNull.Value)
                                    {
                                        newExtracurricularScore = Convert.ToDecimal(result);
                                    }
                                    else
                                    {
                                        newExtracurricularScore = 0;
                                    }
                                }
                                
                                _logger.LogInformation($"Recalculated from scratch for student {studentId}: {newExtracurricularScore}");
                            }
                            
                            // Update the database with the new score
                            bool updated = false;
                            
                            string updateByIdQuery = "UPDATE StudentDetails SET ExtracurricularScore = @ExtracurricularScore WHERE IdNumber = @StudentId";
                            using (var command = new SqlCommand(updateByIdQuery, connection))
                            {
                                command.Parameters.AddWithValue("@ExtracurricularScore", newExtracurricularScore);
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                int rowsAffected = await command.ExecuteNonQueryAsync();
                                
                                if (rowsAffected > 0)
                                {
                                    updated = true;
                                }
                            }
                            
                            if (updated)
                            {
                                _logger.LogInformation($"Updated ExtracurricularScore for student {studentId} to {newExtracurricularScore} with new weight {newWeight}%");
                                successCount++;
                                
                                // Recalculate the overall score
                                await RecalculateScore(connection, studentId);
                            }
                            else
                            {
                                _logger.LogWarning($"Failed to update ExtracurricularScore for student {studentId}");
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error updating extracurricular score for student {studentId}");
                            failCount++;
                        }
                    }
                    
                    return Json(new { 
                        success = true,
                        updated = successCount,
                        failed = failCount,
                        oldWeight = oldWeight,
                        newWeight = newWeight,
                        message = $"Updated extracurricular scores for {successCount} students. Old weight: {oldWeight}%, New weight: {newWeight}%. Failed for {failCount} students." 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating all extracurricular scores");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("GetScoreWeights")]
        public async Task<IActionResult> GetScoreWeightsEndpoint()
        {
            try
            {
                _logger.LogInformation("Fetching score weights from database");
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get weights from the database
                    Dictionary<string, decimal> weights = await GetScoreWeights(connection);
                    
                    return Json(new { 
                        success = true,
                        weights = weights
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching score weights");
                return Json(new { 
                    success = false, 
                    message = ex.Message,
                    // Provide default weights even in error case
                    weights = new Dictionary<string, decimal>
                    {
                        { "AcademicGrades", 30.0m },
                        { "CompletedChallenges", 20.0m },
                        { "Mastery", 20.0m },
                        { "SeminarsWebinars", 10.0m },
                        { "Extracurricular", 20.0m }
                    }
                });
            }
        }
    }
}
