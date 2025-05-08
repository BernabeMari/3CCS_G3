// Create in StudentBadge/Services/DatabaseUtilityService.cs
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace StudentBadge.Services
{
    public class DatabaseUtilityService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseUtilityService> _logger;

        public DatabaseUtilityService(IConfiguration configuration, ILogger<DatabaseUtilityService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public async Task<bool> TableExists(SqlConnection connection, string tableName)
        {
            string query = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }

        public async Task<bool> ColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            string query = @"
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);
                command.Parameters.AddWithValue("@ColumnName", columnName);
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
        }
    }
}