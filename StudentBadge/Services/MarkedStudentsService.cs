using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StudentBadge.Models;

namespace StudentBadge.Services
{
    public class MarkedStudentsService
    {
        private readonly string _connectionString;
        private readonly ILogger<MarkedStudentsService> _logger;

        public MarkedStudentsService(IConfiguration configuration, ILogger<MarkedStudentsService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public async Task<bool> MarkStudent(string employerId, string studentId, string notes = null)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if the student is already marked by this employer
                    string checkQuery = @"
                        SELECT COUNT(1) FROM MarkedStudents 
                        WHERE EmployerId = @EmployerId AND StudentId = @StudentId";
                    
                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@EmployerId", employerId);
                        checkCommand.Parameters.AddWithValue("@StudentId", studentId);
                        
                        int count = (int)await checkCommand.ExecuteScalarAsync();
                        
                        if (count > 0)
                        {
                            // Student already marked, update the notes
                            string updateQuery = @"
                                UPDATE MarkedStudents 
                                SET Notes = @Notes 
                                WHERE EmployerId = @EmployerId AND StudentId = @StudentId";
                            
                            using (var updateCommand = new SqlCommand(updateQuery, connection))
                            {
                                updateCommand.Parameters.AddWithValue("@EmployerId", employerId);
                                updateCommand.Parameters.AddWithValue("@StudentId", studentId);
                                updateCommand.Parameters.AddWithValue("@Notes", notes ?? (object)DBNull.Value);
                                
                                await updateCommand.ExecuteNonQueryAsync();
                                return true;
                            }
                        }
                        else
                        {
                            // Student not marked yet, insert new record
                            string insertQuery = @"
                                INSERT INTO MarkedStudents (EmployerId, StudentId, Notes, DateMarked)
                                VALUES (@EmployerId, @StudentId, @Notes, GETDATE())";
                            
                            using (var insertCommand = new SqlCommand(insertQuery, connection))
                            {
                                insertCommand.Parameters.AddWithValue("@EmployerId", employerId);
                                insertCommand.Parameters.AddWithValue("@StudentId", studentId);
                                insertCommand.Parameters.AddWithValue("@Notes", notes ?? (object)DBNull.Value);
                                
                                await insertCommand.ExecuteNonQueryAsync();
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking student {StudentId} by employer {EmployerId}", studentId, employerId);
                return false;
            }
        }

        public async Task<bool> UnmarkStudent(string employerId, string studentId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string query = @"
                        DELETE FROM MarkedStudents 
                        WHERE EmployerId = @EmployerId AND StudentId = @StudentId";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        await command.ExecuteNonQueryAsync();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unmarking student {StudentId} by employer {EmployerId}", studentId, employerId);
                return false;
            }
        }

        public async Task<List<MarkedStudent>> GetMarkedStudents(string employerId)
        {
            var markedStudents = new List<MarkedStudent>();
            
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    string query = @"
                        SELECT ms.Id, ms.EmployerId, ms.StudentId, ms.DateMarked, ms.Notes,
                               u.FullName, sd.Course, sd.Section, sd.Score, 
                               sd.BadgeColor, sd.ProfilePicturePath
                        FROM MarkedStudents ms
                        LEFT JOIN StudentDetails sd ON ms.StudentId = sd.IdNumber
                        LEFT JOIN Users u ON sd.UserId = u.UserId
                        WHERE ms.EmployerId = @EmployerId
                        ORDER BY ms.DateMarked DESC";
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                markedStudents.Add(new MarkedStudent
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    EmployerId = reader["EmployerId"].ToString(),
                                    StudentId = reader["StudentId"].ToString(),
                                    DateMarked = reader["DateMarked"] != DBNull.Value ? Convert.ToDateTime(reader["DateMarked"]) : DateTime.Now,
                                    Notes = reader["Notes"] != DBNull.Value ? reader["Notes"].ToString() : null,
                                    StudentName = reader["FullName"] != DBNull.Value ? reader["FullName"].ToString() : "Unknown Student",
                                    Course = reader["Course"] != DBNull.Value ? reader["Course"].ToString() : "Unknown",
                                    Section = reader["Section"] != DBNull.Value ? reader["Section"].ToString() : "",
                                    Score = reader["Score"] != DBNull.Value ? Convert.ToDouble(reader["Score"]) : 0,
                                    BadgeColor = reader["BadgeColor"] != DBNull.Value ? reader["BadgeColor"].ToString() : "None",
                                    ProfilePicturePath = reader["ProfilePicturePath"] != DBNull.Value ? reader["ProfilePicturePath"].ToString() : null
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting marked students for employer {EmployerId}", employerId);
            }
            
            return markedStudents;
        }
    }
} 