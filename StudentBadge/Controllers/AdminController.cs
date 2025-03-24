using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using StudentBadge.Models;
using StudentBadge.Data;

namespace StudentBadge.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly StudentContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(StudentContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            // Configure EPPlus to use noncommercial license
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public IActionResult Index()
        {
            ViewBag.TotalStudents = _context.Students.Count();
            return View();
        }

        public IActionResult ImportStudents()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ImportStudents(IFormFile file)
        {
            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return View();
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select an Excel file (.xlsx).";
                return View();
            }

            var list = new List<Student>();
            var successCount = 0;
            var errorCount = 0;
            var errors = new List<string>();

            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        var rowCount = worksheet.Dimension.Rows;

                        // Skip header row (row 1)
                        for (int row = 2; row <= rowCount; row++)
                        {
                            try
                            {
                                var student = new Student
                                {
                                    FullName = worksheet.Cells[row, 1].Value?.ToString()?.Trim(),
                                    Username = worksheet.Cells[row, 2].Value?.ToString()?.Trim(),
                                    Password = worksheet.Cells[row, 3].Value?.ToString()?.Trim(),
                                    IdNumber = worksheet.Cells[row, 4].Value?.ToString()?.Trim(),
                                    Course = worksheet.Cells[row, 5].Value?.ToString()?.Trim(),
                                    Section = worksheet.Cells[row, 6].Value?.ToString()?.Trim(),
                                    IsProfileVisible = true,
                                    IsResumeVisible = true,
                                    Score = 0,
                                    BadgeColor = "green"
                                };

                                // Validate required fields
                                if (string.IsNullOrEmpty(student.FullName) || 
                                    string.IsNullOrEmpty(student.Username) || 
                                    string.IsNullOrEmpty(student.Password) ||
                                    string.IsNullOrEmpty(student.IdNumber) || 
                                    string.IsNullOrEmpty(student.Course))
                                {
                                    errors.Add($"Row {row}: Missing required fields (Full Name, Username, Password, ID Number, or Course)");
                                    errorCount++;
                                    continue;
                                }

                                // Check if student with this ID already exists
                                if (_context.Students.Any(s => s.IdNumber == student.IdNumber))
                                {
                                    errors.Add($"Row {row}: Student with ID {student.IdNumber} already exists");
                                    errorCount++;
                                    continue;
                                }

                                // Check if username already exists
                                if (_context.Students.Any(s => s.Username == student.Username))
                                {
                                    errors.Add($"Row {row}: Username {student.Username} already exists");
                                    errorCount++;
                                    continue;
                                }

                                _context.Students.Add(student);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {row}: {ex.Message}");
                                errorCount++;
                            }
                        }

                        await _context.SaveChangesAsync();
                    }
                }

                TempData["Success"] = $"Successfully imported {successCount} student records.";
                if (errorCount > 0)
                {
                    TempData["ErrorList"] = string.Join("<br/>", errors);
                }

                return RedirectToAction(nameof(ImportStudents));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error: {ex.Message}";
                return View();
            }
        }

        public IActionResult Students()
        {
            var students = _context.Students.ToList();
            return View(students);
        }

        public IActionResult DownloadTemplate()
        {
            var stream = new MemoryStream();
            using (var package = new ExcelPackage(stream))
            {
                var worksheet = package.Workbook.Worksheets.Add("Students");
                
                // Add header row with all required columns
                worksheet.Cells[1, 1].Value = "Full Name";
                worksheet.Cells[1, 2].Value = "Username";
                worksheet.Cells[1, 3].Value = "Password";
                worksheet.Cells[1, 4].Value = "ID Number";
                worksheet.Cells[1, 5].Value = "Course";
                worksheet.Cells[1, 6].Value = "Section";
                
                // Format header row with bold font and background color
                using (var range = worksheet.Cells[1, 1, 1, 6])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Add sample data
                worksheet.Cells[2, 1].Value = "John Doe";
                worksheet.Cells[2, 2].Value = "john.doe";
                worksheet.Cells[2, 3].Value = "password123";
                worksheet.Cells[2, 4].Value = "2023001";
                worksheet.Cells[2, 5].Value = "Computer Science";
                worksheet.Cells[2, 6].Value = "A";
                
                worksheet.Cells[3, 1].Value = "Jane Smith";
                worksheet.Cells[3, 2].Value = "jane.smith";
                worksheet.Cells[3, 3].Value = "password456";
                worksheet.Cells[3, 4].Value = "2023002";
                worksheet.Cells[3, 5].Value = "Information Technology";
                worksheet.Cells[3, 6].Value = "B";
                
                // Auto-fit columns
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                
                package.Save();
            }
            
            stream.Position = 0;
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "StudentImportTemplate.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStudent(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Json(new { success = false, message = "Student ID is required" });
            }

            try
            {
                var student = await _context.Students.FindAsync(id);
                if (student == null)
                {
                    return Json(new { success = false, message = "Student not found" });
                }

                _context.Students.Remove(student);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult AdminDashboard()
        {
            ViewBag.TotalStudents = _context.Students.Count();
            var students = _context.Students.ToList();
            return View(students);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStudent(string IdNumber, string FullName, string Course, string Section, string BadgeColor)
        {
            if (string.IsNullOrEmpty(IdNumber))
            {
                return Json(new { success = false, message = "Student ID is required" });
            }

            try
            {
                var student = await _context.Students.FindAsync(IdNumber);
                if (student == null)
                {
                    return Json(new { success = false, message = "Student not found" });
                }

                student.FullName = FullName;
                student.Course = Course;
                student.Section = Section;
                student.BadgeColor = BadgeColor;

                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
} 