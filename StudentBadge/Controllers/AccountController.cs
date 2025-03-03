using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class AccountController : Controller
{
    private readonly IConfiguration _configuration;

    public AccountController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> Signup(string username, string fullName, string password, string role,
                                         string idNumber, string course, string section, string company)
    {
        if (string.IsNullOrEmpty(role))
        {
            ViewBag.ErrorMessage = "Role is required.";
            return View("~/Views/Home/Signup.cshtml"); 
        }

        using (var connection = new SqlConnection(_configuration.GetConnectionString("YourConnectionString")))
        {
            await connection.OpenAsync();

            // Check if username already exists
            string checkUsernameQuery = "SELECT COUNT(*) FROM dbo.Users WHERE Username = @Username";
            using (var usernameCheckCommand = new SqlCommand(checkUsernameQuery, connection))
            {
                usernameCheckCommand.Parameters.AddWithValue("@Username", username);
                int usernameExists = (int)await usernameCheckCommand.ExecuteScalarAsync();

                if (usernameExists > 0)
                {
                    ViewBag.ErrorMessage = "Username already exists.";
                    return View("~/Views/Home/Signup.cshtml"); 
                }
            }

            // Check IdBumber already exists (for students)
            if (role == "student")
            {
                string checkIdNumberQuery = "SELECT COUNT(*) FROM dbo.Users WHERE IdNumber = @IdNumber";
                using (var idNumberCheckCommand = new SqlCommand(checkIdNumberQuery, connection))
                {
                    idNumberCheckCommand.Parameters.AddWithValue("@IdNumber", idNumber);
                    int idNumberExists = (int)await idNumberCheckCommand.ExecuteScalarAsync();

                    if (idNumberExists > 0)
                    {
                        ViewBag.ErrorMessage = "ID Number already exists.";
                        return View("~/Views/Home/Signup.cshtml"); 
                    }
                }
            }

            // Insert user to database
            string query = "INSERT INTO dbo.Users (Username, FullName, Password, Role, IdNumber, Course, Section, Company) " +
                           "VALUES (@Username, @FullName, @Password, @Role, @IdNumber, @Course, @Section, @Company)";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@FullName", fullName);
                command.Parameters.AddWithValue("@Password", password); 
                command.Parameters.AddWithValue("@Role", role);

                if (role == "student")
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    command.Parameters.AddWithValue("@Course", course);
                    command.Parameters.AddWithValue("@Section", section);
                    command.Parameters.AddWithValue("@Company", DBNull.Value);
                }
                else if (role == "employer")
                {
                    command.Parameters.AddWithValue("@IdNumber", DBNull.Value);
                    command.Parameters.AddWithValue("@Course", DBNull.Value);
                    command.Parameters.AddWithValue("@Section", DBNull.Value);
                    command.Parameters.AddWithValue("@Company", company);
                }
                else if (role == "admin")
                {
                    command.Parameters.AddWithValue("@IdNumber", DBNull.Value);
                    command.Parameters.AddWithValue("@Course", DBNull.Value);
                    command.Parameters.AddWithValue("@Section", DBNull.Value);
                    command.Parameters.AddWithValue("@Company", DBNull.Value);
                }

                await command.ExecuteNonQueryAsync();
            }
        }

        ViewBag.SuccessMessage = "Signup successful! You can now login.";
        return View("~/Views/Home/Signup.cshtml"); 
    }


    [HttpPost]
    public async Task<IActionResult> Login(string username, string password)
    {
        using (var connection = new SqlConnection(_configuration.GetConnectionString("YourConnectionString")))
        {
            await connection.OpenAsync();

            // Fetch user info
            string query = "SELECT Password FROM dbo.Users WHERE Username = @Username";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                var storedPassword = await command.ExecuteScalarAsync();

                if (storedPassword == null)
                {
                    ViewBag.Error = "No user exists.";
                    return View("~/Views/Home/Login.cshtml"); 
                }

                if (storedPassword.ToString() == password)
                {
                    return RedirectToAction("Index", "Home"); 
                }
                else
                {
                    ViewBag.Error = "Invalid username or password.";
                    return View("~/Views/Home/Login.cshtml");
                }
            }
        }
    }

}
