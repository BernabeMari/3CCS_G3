using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace StudentBadge.Services
{
    public class BadgeService
    {
        private readonly string _connectionString;
        private readonly ILogger<BadgeService> _logger;

        public BadgeService(IConfiguration configuration, ILogger<BadgeService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        /// <summary>
        /// Updates the badge color for a student based on their current score
        /// </summary>
        public async Task UpdateBadgeColor(string studentId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // First, get the student's current score
                    string getScoreQuery = @"
                        SELECT Score 
                        FROM StudentDetails 
                        WHERE IdNumber = @StudentId";

                    decimal score = 0;
                    using (var command = new SqlCommand(getScoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            score = Convert.ToDecimal(result);
                        }
                    }

                    // Calculate badge color using the correct thresholds
                    string badgeColor = score >= 95 ? "platinum" : 
                                      score >= 85 ? "gold" : 
                                      score >= 75 ? "silver" : 
                                      score >= 65 ? "bronze" : 
                                      score >= 50 ? "rising-star" : 
                                      score >= 1 ? "needs" : "none";

                    // Update the badge color in StudentDetails
                    string updateQuery = @"
                        UPDATE StudentDetails 
                        SET BadgeColor = @BadgeColor 
                        WHERE IdNumber = @StudentId";

                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        command.Parameters.AddWithValue("@BadgeColor", badgeColor);
                        await command.ExecuteNonQueryAsync();
                    }

                    // Check if we're using the old database structure and update it too
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'Students'";

                    bool oldTableExists = false;
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        oldTableExists = (count > 0);
                    }

                    if (oldTableExists)
                    {
                        string updateOldQuery = @"
                            UPDATE Students 
                            SET BadgeColor = @BadgeColor 
                            WHERE IdNumber = @StudentId";

                        using (var command = new SqlCommand(updateOldQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentId", studentId);
                            command.Parameters.AddWithValue("@BadgeColor", badgeColor);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating badge color for student {studentId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the appropriate badge color for a given score
        /// </summary>
        public static string GetBadgeColorForScore(decimal score)
        {
            return score >= 95 ? "platinum" : 
                   score >= 85 ? "gold" : 
                   score >= 75 ? "silver" : 
                   score >= 65 ? "bronze" : 
                   score >= 50 ? "rising-star" : 
                   score >= 1 ? "needs" : "none";
        }
        
        /// <summary>
        /// Gets the URL for a student's badge image based on their badge color
        /// </summary>
        /// <param name="studentId">The student's ID number</param>
        /// <returns>The URL to the badge image</returns>
        public async Task<string> GetStudentBadgeUrl(string studentId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Get the student's badge color
                    string getBadgeColorQuery = @"
                        SELECT BadgeColor 
                        FROM StudentDetails 
                        WHERE IdNumber = @StudentId";

                    string badgeColor = "none";
                    using (var command = new SqlCommand(getBadgeColorQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            badgeColor = result.ToString();
                        }
                    }

                    // Return the appropriate badge image URL based on the badge color
                    return GetBadgeImageUrl(badgeColor);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting badge URL for student {studentId}: {ex.Message}");
                return "/images/badges/default.png"; // Return a default badge URL if an error occurs
            }
        }
        
        /// <summary>
        /// Gets the image URL for a specific badge color
        /// </summary>
        /// <param name="badgeColor">The badge color</param>
        /// <returns>The URL to the badge image</returns>
        private string GetBadgeImageUrl(string badgeColor)
        {
            switch (badgeColor.ToLower())
            {
                case "platinum":
                    return "/images/badges/platinum.png";
                case "gold":
                    return "/images/badges/gold.png";
                case "silver":
                    return "/images/badges/silver.png";
                case "bronze":
                    return "/images/badges/bronze.png";
                case "rising-star":
                    return "/images/badges/rising-star.png";
                case "needs":
                    return "/images/badges/needs.png";
                default:
                    return "/images/badges/default.png";
            }
        }
    }
} 