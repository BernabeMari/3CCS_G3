using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Dynamic;

namespace StudentBadge.Controllers
{
    [Route("[controller]")]
    public class ExtraCurricularController : Controller
    {
        private readonly ILogger<ExtraCurricularController> _logger;
        private readonly string _connectionString;

        public ExtraCurricularController(ILogger<ExtraCurricularController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        [Route("GetExtraCurricularActivities/{studentId}")]
        public async Task<IActionResult> GetExtraCurricularActivities(string studentId)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId))
                {
                    return Json(new { success = false, message = "Student ID is required." });
                }
                
                List<dynamic> activities = new List<dynamic>();
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "ExtraCurricularActivities"))
                    {
                        await EnsureExtraCurricularActivitiesTableExists(connection);
                        return Json(new { success = true, activities = activities });
                    }
                    
                    string query = @"
                        SELECT 
                            ActivityId, 
                            ActivityName, 
                            ActivityType, 
                            Description, 
                            StartDate, 
                            EndDate, 
                            RoleInActivity, 
                            AchievementsNotes
                        FROM 
                            ExtraCurricularActivities 
                        WHERE 
                            StudentId = @StudentId 
                        ORDER BY 
                            StartDate DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                dynamic activity = new ExpandoObject();
                                activity.id = Convert.ToInt32(reader["ActivityId"]);
                                activity.name = reader["ActivityName"].ToString();
                                activity.type = reader["ActivityType"].ToString();
                                activity.description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader["Description"].ToString();
                                activity.startDate = reader.IsDBNull(reader.GetOrdinal("StartDate")) ? null : Convert.ToDateTime(reader["StartDate"]).ToString("yyyy-MM-dd");
                                activity.endDate = reader.IsDBNull(reader.GetOrdinal("EndDate")) ? null : Convert.ToDateTime(reader["EndDate"]).ToString("yyyy-MM-dd");
                                activity.role = reader.IsDBNull(reader.GetOrdinal("RoleInActivity")) ? null : reader["RoleInActivity"].ToString();
                                activity.achievements = reader.IsDBNull(reader.GetOrdinal("AchievementsNotes")) ? null : reader["AchievementsNotes"].ToString();
                                
                                activities.Add(activity);
                            }
                        }
                    }
                }
                
                return Json(new { success = true, activities = activities });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting extracurricular activities for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpPost]
        [Route("AddExtraCurricularActivity")]
        public async Task<IActionResult> AddExtraCurricularActivity(string studentId, string activityName, string activityType, 
            string description, string startDate, string endDate, string role, string achievements)
        {
            try
            {
                if (string.IsNullOrEmpty(studentId) || string.IsNullOrEmpty(activityName) || string.IsNullOrEmpty(activityType))
                {
                    return Json(new { success = false, message = "Student ID, Activity Name, and Activity Type are required." });
                }
                
                DateTime? startDateValue = null;
                if (!string.IsNullOrEmpty(startDate))
                {
                    DateTime parsedStartDate;
                    if (DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedStartDate))
                    {
                        startDateValue = parsedStartDate;
                    }
                }
                
                DateTime? endDateValue = null;
                if (!string.IsNullOrEmpty(endDate))
                {
                    DateTime parsedEndDate;
                    if (DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEndDate))
                    {
                        endDateValue = parsedEndDate;
                    }
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Ensure the table exists
                    await EnsureExtraCurricularActivitiesTableExists(connection);
                    
                    string insertQuery = @"
                        INSERT INTO ExtraCurricularActivities (
                            StudentId,
                            ActivityName,
                            ActivityType,
                            Description,
                            StartDate,
                            EndDate,
                            RoleInActivity,
                            AchievementsNotes
                        ) VALUES (
                            @StudentId,
                            @ActivityName,
                            @ActivityType,
                            @Description,
                            @StartDate,
                            @EndDate,
                            @Role,
                            @Achievements
                        );
                        SELECT SCOPE_IDENTITY();";
                    
                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        command.Parameters.AddWithValue("@ActivityName", activityName);
                        command.Parameters.AddWithValue("@ActivityType", activityType);
                        command.Parameters.AddWithValue("@Description", string.IsNullOrEmpty(description) ? (object)DBNull.Value : description);
                        command.Parameters.AddWithValue("@StartDate", startDateValue.HasValue ? (object)startDateValue.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@EndDate", endDateValue.HasValue ? (object)endDateValue.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Role", string.IsNullOrEmpty(role) ? (object)DBNull.Value : role);
                        command.Parameters.AddWithValue("@Achievements", string.IsNullOrEmpty(achievements) ? (object)DBNull.Value : achievements);
                        
                        var newId = await command.ExecuteScalarAsync();
                        
                        return Json(new { success = true, message = "Activity added successfully.", activityId = Convert.ToInt32(newId) });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding extracurricular activity for student {StudentId}", studentId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpPost]
        [Route("UpdateExtraCurricularActivity")]
        public async Task<IActionResult> UpdateExtraCurricularActivity(int activityId, string activityName, string activityType, 
            string description, string startDate, string endDate, string role, string achievements)
        {
            try
            {
                if (activityId <= 0 || string.IsNullOrEmpty(activityName) || string.IsNullOrEmpty(activityType))
                {
                    return Json(new { success = false, message = "Activity ID, Activity Name, and Activity Type are required." });
                }
                
                DateTime? startDateValue = null;
                if (!string.IsNullOrEmpty(startDate))
                {
                    DateTime parsedStartDate;
                    if (DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedStartDate))
                    {
                        startDateValue = parsedStartDate;
                    }
                }
                
                DateTime? endDateValue = null;
                if (!string.IsNullOrEmpty(endDate))
                {
                    DateTime parsedEndDate;
                    if (DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEndDate))
                    {
                        endDateValue = parsedEndDate;
                    }
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "ExtraCurricularActivities"))
                    {
                        return Json(new { success = false, message = "Extra-curricular activities table does not exist." });
                    }
                    
                    // Check if the activity exists
                    string checkQuery = "SELECT COUNT(*) FROM ExtraCurricularActivities WHERE ActivityId = @ActivityId";
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@ActivityId", activityId);
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        
                        if (count == 0)
                        {
                            return Json(new { success = false, message = "Activity not found." });
                        }
                    }
                    
                    string updateQuery = @"
                        UPDATE ExtraCurricularActivities
                        SET 
                            ActivityName = @ActivityName,
                            ActivityType = @ActivityType,
                            Description = @Description,
                            StartDate = @StartDate,
                            EndDate = @EndDate,
                            RoleInActivity = @Role,
                            AchievementsNotes = @Achievements
                        WHERE ActivityId = @ActivityId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ActivityId", activityId);
                        command.Parameters.AddWithValue("@ActivityName", activityName);
                        command.Parameters.AddWithValue("@ActivityType", activityType);
                        command.Parameters.AddWithValue("@Description", string.IsNullOrEmpty(description) ? (object)DBNull.Value : description);
                        command.Parameters.AddWithValue("@StartDate", startDateValue.HasValue ? (object)startDateValue.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@EndDate", endDateValue.HasValue ? (object)endDateValue.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Role", string.IsNullOrEmpty(role) ? (object)DBNull.Value : role);
                        command.Parameters.AddWithValue("@Achievements", string.IsNullOrEmpty(achievements) ? (object)DBNull.Value : achievements);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Activity updated successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Failed to update activity." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating extracurricular activity {ActivityId}", activityId);
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpDelete]
        [Route("DeleteExtraCurricularActivity/{id}")]
        public async Task<IActionResult> DeleteExtraCurricularActivity(int id)
        {
            try
            {
                if (id <= 0)
                {
                    return Json(new { success = false, message = "Activity ID is required." });
                }
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the table exists
                    if (!await TableExists(connection, "ExtraCurricularActivities"))
                    {
                        return Json(new { success = false, message = "Extra-curricular activities table does not exist." });
                    }
                    
                    string deleteQuery = "DELETE FROM ExtraCurricularActivities WHERE ActivityId = @ActivityId";
                    
                    using (var command = new SqlCommand(deleteQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ActivityId", id);
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Activity deleted successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Activity not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting extracurricular activity {ActivityId}", id);
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
        
        // Helper method to ensure ExtraCurricularActivities table exists
        private async Task EnsureExtraCurricularActivitiesTableExists(SqlConnection connection)
        {
            if (!await TableExists(connection, "ExtraCurricularActivities"))
            {
                string createTableSql = @"
                    CREATE TABLE ExtraCurricularActivities (
                        ActivityId INT IDENTITY(1,1) PRIMARY KEY,
                        StudentId NVARCHAR(128) NOT NULL,
                        ActivityName NVARCHAR(255) NOT NULL,
                        ActivityType NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(MAX) NULL,
                        StartDate DATE NULL,
                        EndDate DATE NULL,
                        RoleInActivity NVARCHAR(255) NULL,
                        AchievementsNotes NVARCHAR(MAX) NULL
                    )";
                
                using (var command = new SqlCommand(createTableSql, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}