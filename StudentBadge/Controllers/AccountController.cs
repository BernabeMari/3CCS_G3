using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StudentBadge.Models;
using Newtonsoft.Json;

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

            string tableName = role switch
            {
                "student" => "dbo.Students",
                "admin" => "dbo.Admins",
                "employer" => "dbo.Employers",
                _ => null
            };

            if (tableName == null)
            {
                ViewBag.ErrorMessage = "Invalid role.";
                return View("~/Views/Home/Signup.cshtml");
            }

            // Check if username already exists in the correct table
            string checkUsernameQuery = $"SELECT COUNT(*) FROM {tableName} WHERE Username = @Username";
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

            // Check if IdNumber exists for students
            if (role == "student")
            {
                string checkIdNumberQuery = "SELECT COUNT(*) FROM dbo.Students WHERE IdNumber = @IdNumber";
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

            // Generate EmployerId for employer accounts
            string employerId = null;
            if (role == "employer")
            {
                // Generate a unique EmployerId (EMP + timestamp + random number)
                employerId = $"EMP{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                
                // Verify the generated ID is unique
                string checkEmployerIdQuery = "SELECT COUNT(*) FROM dbo.Employers WHERE EmployerId = @EmployerId";
                using (var employerIdCheckCommand = new SqlCommand(checkEmployerIdQuery, connection))
                {
                    bool isUnique = false;
                    while (!isUnique)
                    {
                        employerIdCheckCommand.Parameters.Clear();
                        employerIdCheckCommand.Parameters.AddWithValue("@EmployerId", employerId);
                        int employerIdExists = (int)await employerIdCheckCommand.ExecuteScalarAsync();
                        
                        if (employerIdExists == 0)
                        {
                            isUnique = true;
                        }
                        else
                        {
                            employerId = $"EMP{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
                        }
                    }
                }
            }

            // Build INSERT statement based on role
            string query = role switch
            {
                "student" => "INSERT INTO dbo.Students (Username, FullName, Password, IdNumber, Course, Section) " +
"VALUES (@Username, @FullName, @Password, @IdNumber, @Course, @Section)",


                "employer" => "INSERT INTO dbo.Employers (EmployerId, Username, FullName, Password, Company) " +
                              "VALUES (@EmployerId, @Username, @FullName, @Password, @Company)",

                "admin" => "INSERT INTO dbo.Admins (Username, FullName, Password) " +
                           "VALUES (@Username, @FullName, @Password)",

                _ => null
            };

            if (query == null)
            {
                ViewBag.ErrorMessage = "Invalid role.";
                return View("~/Views/Home/Signup.cshtml");
            }

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@FullName", fullName);
                command.Parameters.AddWithValue("@Password", password);

                if (role == "student")
                {
                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                    command.Parameters.AddWithValue("@Course", course);
                    command.Parameters.AddWithValue("@Section", section);
                }
                else if (role == "employer")
                {
                    command.Parameters.AddWithValue("@EmployerId", employerId);
                    command.Parameters.AddWithValue("@Company", company);
                }

                await command.ExecuteNonQueryAsync();
            }
        }

        ViewBag.SuccessMessage = "Signup successful! You can now log in.";
        return View("~/Views/Home/Signup.cshtml");
    }


    [HttpPost]
    public async Task<IActionResult> Login(string role, string username, string password)
    {
        if (string.IsNullOrEmpty(role) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ViewBag.Error = "All fields are required.";
            return View("~/Views/Home/Login.cshtml");
        }

        using (var connection = new SqlConnection(_configuration.GetConnectionString("YourConnectionString")))
        {
            await connection.OpenAsync();

            // Determine correct table based on role
            string tableName = role switch
            {
                "student" => "dbo.Students",
                "admin" => "dbo.Admins",
                "employer" => "dbo.Employers",
                _ => null
            };

            if (tableName == null)
            {
                ViewBag.Error = "Invalid role selected.";
                return View("~/Views/Home/Login.cshtml");
            }

            // Query to check credentials
            string query = $"SELECT Password FROM {tableName} WHERE Username = @Username";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Username", username);
                var storedPassword = await command.ExecuteScalarAsync();

                if (storedPassword == null || storedPassword.ToString() != password)
                {
                    ViewBag.Error = "Invalid username, role, or password.";
                    return View("~/Views/Home/Login.cshtml");
                }
            }

            // Fetch student information if role is student
            if (role == "student")
            {
                string studentQuery = "SELECT FullName, IdNumber, Course, Section FROM dbo.Students WHERE Username = @Username";
                using (var studentCommand = new SqlCommand(studentQuery, connection))
                {
                    studentCommand.Parameters.AddWithValue("@Username", username);
                    using (var reader = await studentCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Store student info in session
                            HttpContext.Session.SetString("FullName", reader["FullName"].ToString());
                            HttpContext.Session.SetString("IdNumber", reader["IdNumber"].ToString());
                            HttpContext.Session.SetString("Course", reader["Course"].ToString());
                            HttpContext.Session.SetString("Section", reader["Section"].ToString());
                        }
                    }
                }
            }
            // Add employer information if role is employer
            else if (role == "employer")
            {
                string employerQuery = "SELECT EmployerId, FullName, Company FROM dbo.Employers WHERE Username = @Username";
                using (var employerCommand = new SqlCommand(employerQuery, connection))
                {
                    employerCommand.Parameters.AddWithValue("@Username", username);
                    using (var reader = await employerCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            // Store employer info in session
                            HttpContext.Session.SetString("EmployerId", reader["EmployerId"].ToString());
                            HttpContext.Session.SetString("EmployerName", reader["FullName"].ToString());
                            HttpContext.Session.SetString("CompanyName", reader["Company"].ToString());
                        }
                    }
                }
            }

            // Store session for authenticated user
            HttpContext.Session.SetString("Username", username);
            HttpContext.Session.SetString("Role", role);

            // Declare the list before using it
            var allStudents = new List<Student>();

            string allStudentsQuery = "SELECT FullName, IdNumber, Course, Section, Score, Achievements, Comments, BadgeColor FROM dbo.Students ORDER BY Score ASC";

            using (var studentsCommand = new SqlCommand(allStudentsQuery, connection))
            using (var reader = await studentsCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    allStudents.Add(new Student
                    {
                        FullName = reader["FullName"].ToString(),
                        IdNumber = reader["IdNumber"].ToString(),
                        Course = reader["Course"].ToString(),
                        Section = reader["Section"].ToString(),
                        Score = Convert.ToInt32(reader["Score"])
                    });
                }
            }

            // Store the sorted students list in ViewBag
            HttpContext.Session.SetString("AllStudents", JsonConvert.SerializeObject(allStudents));


        }



        // Redirect based on role
        return role switch
            {
                "admin" => RedirectToAction("AdminDashboard", "Dashboard"),
                "student" => RedirectToAction("StudentDashboard", "Dashboard"),
                "employer" => RedirectToAction("EmployerDashboard", "Dashboard"),
                _ => RedirectToAction("Index", "Home")
            };
        }
    }



