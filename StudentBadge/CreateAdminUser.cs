using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StudentBadge.Services;

namespace StudentBadge.Utils
{
    public class CreateAdminUser
    {
        public static async Task CreateAdmin(IConfiguration configuration)
        {
            string username = "zyb";
            string password = "Bernabe202003!";
            string fullName = "Admin User";
            string role = "admin";
            string email = "admin@example.com";

            // Hash the password
            string hashedPassword = PasswordHashService.HashPassword(password);

            // Generate admin ID with timestamp
            string adminId = $"ADMIN{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";

            // Use connection string from configuration
            string connectionString = configuration.GetConnectionString("DefaultConnection");
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                // Check if username already exists
                string checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                using (var command = new SqlCommand(checkQuery, connection))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    int count = (int)await command.ExecuteScalarAsync();
                    if (count > 0)
                    {
                        Console.WriteLine($"Admin user '{username}' already exists.");
                        return;
                    }
                }

                // Insert the admin user with hashed password
                string insertQuery = @"
                    INSERT INTO Users (UserId, Username, Password, FullName, Role, Email, IsActive, IsVerified)
                    VALUES (@UserId, @Username, @Password, @FullName, @Role, @Email, 1, 1)";

                using (var command = new SqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@UserId", adminId);
                    command.Parameters.AddWithValue("@Username", username);
                    command.Parameters.AddWithValue("@Password", hashedPassword);
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@Role", role);
                    command.Parameters.AddWithValue("@Email", email);
                    
                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine($"Admin user '{username}' created successfully.");
                }
            }
        }
    }
} 