using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Linq;
using Newtonsoft.Json;
using System.Drawing;
using Microsoft.AspNetCore.Http;

namespace StudentBadge.Controllers
{
    [Route("FileHandler")]
    public class FileController : Controller
    {
        private readonly string _connectionString;

        public FileController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost]
        [Route("SaveProfilePicture")]
        public async Task<IActionResult> SaveProfilePicture([FromBody] ProfilePictureModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.Base64Image))
            {
                return Json(new { success = false, message = "No image data received." });
            }

            try
            {
                string base64Image = model.Base64Image;
                string contentType = "image/jpeg";

                // Handle the case when the full data URL is sent
                if (base64Image.Contains(","))
                {
                    // Extract content type and base64 data
                    string[] parts = base64Image.Split(',');
                    if (parts.Length == 2)
                    {
                        // Store the content type from the data URL
                        if (parts[0].Contains(":") && parts[0].Contains(";"))
                        {
                            contentType = parts[0].Split(':')[1].Split(';')[0];
                        }
                        // Just save the base64 part without the data URL prefix
                        base64Image = parts[1];
                    }
                }

                // Validate that the string is valid base64
                if (!IsValidBase64(base64Image))
                {
                    return Json(new { success = false, message = "Invalid image format." });
                }

                string idNumber = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(idNumber))
                {
                    return Json(new { success = false, message = "User not authenticated." });
                }

                // Convert base64 to bytes
                byte[] imageBytes = Convert.FromBase64String(base64Image);
                
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if the new database structure is being used and if binary columns exist
                    bool usingNewBinaryColumns = false;
                    string checkColumnQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ProfilePictureData'";
                    
                    using (var command = new SqlCommand(checkColumnQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        usingNewBinaryColumns = (count > 0);
                    }
                    
                    string query;
                    int rowsAffected = 0;
                    
                    if (usingNewBinaryColumns)
                    {
                        // Create metadata JSON
                        string metadata = JsonConvert.SerializeObject(new
                        {
                            ContentType = contentType,
                            UploadDate = DateTime.UtcNow,
                            Source = "binary"
                        });
                        
                        // Update StudentDetails table with binary data and metadata
                        query = @"
                            UPDATE StudentDetails 
                            SET ProfilePictureData = @ProfilePictureData,
                                ProfileMetadata = @ProfileMetadata,
                                ProfilePicturePath = @ProfilePicturePath
                            WHERE IdNumber = @IdNumber";
                        
                        using (var command = new SqlCommand(query, connection))
                        {
                            // Store the binary image data
                            command.Parameters.AddWithValue("@ProfilePictureData", imageBytes);
                            command.Parameters.AddWithValue("@ProfileMetadata", metadata);
                            
                            // Keep a reference to content type in the path for backwards compatibility
                            string dataUrl = $"data:{contentType};base64,{base64Image.Substring(0, Math.Min(100, base64Image.Length))}...";
                            command.Parameters.AddWithValue("@ProfilePicturePath", dataUrl);
                            
                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                            rowsAffected = await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Fall back to the old method of storing as a file
                        // Create a unique filename
                        string extension = ".jpg";
                        if (contentType == "image/png") extension = ".png";
                        else if (contentType == "image/gif") extension = ".gif";
                        
                        string fileName = $"{idNumber}_{DateTime.Now.Ticks}{extension}";
                        
                        // Define the directory path
                        string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
                        
                        // Ensure directory exists
                        if (!Directory.Exists(uploadsDir))
                        {
                            Directory.CreateDirectory(uploadsDir);
                        }
                        
                        // Full path to save the file
                        string filePath = Path.Combine(uploadsDir, fileName);
                        
                        // Save the file
                        await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                        
                        // URL to access the file
                        string fileUrl = $"/uploads/profiles/{fileName}";
                        
                        // Check if we should use StudentDetails or Students table
                        bool useStudentDetails = false;
                        string checkTableQuery = @"
                            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                            WHERE TABLE_NAME = 'StudentDetails'";
                        
                        using (var command = new SqlCommand(checkTableQuery, connection))
                        {
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            useStudentDetails = (count > 0);
                        }
                        
                        if (useStudentDetails)
                        {
                            // Update StudentDetails table in the new structure
                            query = @"
                                UPDATE StudentDetails 
                                SET ProfilePicturePath = @ProfilePicturePath
                                WHERE IdNumber = @IdNumber";
                        }
                        else
                        {
                            // Update Students table in the old structure
                            query = @"
                                UPDATE Students 
                                SET ProfilePicturePath = @ProfilePicturePath
                                WHERE IdNumber = @IdNumber";
                        }

                        using (var command = new SqlCommand(query, connection))
                        {
                            // Store just the file path
                            command.Parameters.AddWithValue("@ProfilePicturePath", fileUrl);
                            command.Parameters.AddWithValue("@IdNumber", idNumber);
                            rowsAffected = await command.ExecuteNonQueryAsync();
                        }
                    }

                    if (rowsAffected > 0)
                    {
                        return Json(new
                        {
                            success = true,
                            message = "Profile picture saved successfully.",
                            imageUrl = $"/FileHandler/GetProfilePicture?studentId={idNumber}&t={DateTime.Now.Ticks}" // Use timestamp to prevent caching
                        });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Failed to update profile picture. User not found." });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving profile picture: " + ex.Message });
            }
        }
        
        // Create an extremely small thumbnail to fit in database column
        private string CreateTinyThumbnail(string base64Image)
        {
            try
            {
                // Convert base64 to byte array
                byte[] imageBytes = Convert.FromBase64String(base64Image);
                
                using (var ms = new MemoryStream(imageBytes))
                {
                    using (var image = Image.FromStream(ms))
                    {
                        // Create a very small thumbnail (80x80) to fit in database
                        int size = 80;
                        int width, height;
                        
                        // Calculate aspect ratio to maintain proportions
                        if (image.Width > image.Height)
                        {
                            width = size;
                            height = (int)(image.Height * ((float)size / image.Width));
                        }
                        else
                        {
                            height = size;
                            width = (int)(image.Width * ((float)size / image.Height));
                        }
                        
                        using (var thumbnail = new Bitmap(width, height))
                        {
                            using (var g = Graphics.FromImage(thumbnail))
                            {
                                // Low quality for small file size
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                
                                g.DrawImage(image, 0, 0, width, height);
                            }
                            
                            using (var outStream = new MemoryStream())
                            {
                                // Extremely low quality JPEG (15%) to keep size small
                                var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                                    .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                                
                                var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                                encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                    System.Drawing.Imaging.Encoder.Quality, 15L);
                                
                                thumbnail.Save(outStream, jpegEncoder, encoderParams);
                                return Convert.ToBase64String(outStream.ToArray());
                            }
                        }
                    }
                }
            }
            catch
            {
                try
                {
                    // Ultimate fallback - create a tiny 40x40 image
                    byte[] imageBytes = Convert.FromBase64String(base64Image);
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        using (var image = Image.FromStream(ms))
                        {
                            using (var thumbnail = new Bitmap(40, 40))
                            {
                                using (var g = Graphics.FromImage(thumbnail))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                                    g.DrawImage(image, 0, 0, 40, 40);
                                }
                                
                                using (var outStream = new MemoryStream())
                                {
                                    // Super low quality (10%)
                                    var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                                        .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                                    
                                    var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                                    encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                        System.Drawing.Imaging.Encoder.Quality, 10L);
                                    
                                    thumbnail.Save(outStream, jpegEncoder, encoderParams);
                                    return Convert.ToBase64String(outStream.ToArray());
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // If all else fails, return a tiny 1x1 pixel image
                    return "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
                }
            }
        }

        // Save resume with improved error handling and transaction management
        [HttpPost]
        [Route("SaveResume")]
        public async Task<IActionResult> SaveResume([FromBody] ResumeModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.ResumeFile))
            {
                return Json(new { success = false, message = "No resume file received." });
            }

            try
            {
                string base64File = model.ResumeFile;
                string fileName = model.ResumeFileName;
                string contentType = "application/pdf"; // Default content type

                // Handle the case when the full data URL is sent
                if (base64File.Contains(","))
                {
                    // Extract content type and base64 data
                    string[] parts = base64File.Split(',');
                    if (parts.Length == 2)
                    {
                        // Store the content type from the data URL
                        if (parts[0].Contains(":") && parts[0].Contains(";"))
                        {
                            contentType = parts[0].Split(':')[1].Split(';')[0];
                        }
                        base64File = parts[1];
                    }
                }

                string idNumber = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(idNumber))
                {
                    return Json(new { success = false, message = "User not authenticated." });
                }

                // Convert base64 to bytes
                byte[] fileBytes = Convert.FromBase64String(base64File);
                
                // Use file storage approach to prevent transaction log errors
                string fileReference = $"Resume_{idNumber}_{DateTime.Now.Ticks}";
                string extension = Path.GetExtension(fileName);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = contentType == "application/pdf" ? ".pdf" : ".doc";
                }
                
                // Save file to disk
                string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "resumes");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }
                
                string uniqueFileName = fileReference + extension;
                string filePath = Path.Combine(uploadsDir, uniqueFileName);
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);
                
                // URL path to access the file
                string fileUrl = $"/uploads/resumes/{uniqueFileName}";
                
                // Store reference in the database
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if we're using the binary columns
                    bool hasBinaryColumns = false;
                    string checkColumnQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'ResumeData'";
                    
                    using (var command = new SqlCommand(checkColumnQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        hasBinaryColumns = (count > 0);
                    }
                    
                    // Check if OriginalResumeFileName column exists
                    bool hasOriginalFileNameColumn = false;
                    string checkOriginalFileNameColumnQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'OriginalResumeFileName'";
                    
                    using (var command = new SqlCommand(checkOriginalFileNameColumnQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        hasOriginalFileNameColumn = (count > 0);
                    }
                    
                    int rowsAffected = 0;
                    
                    // Begin transaction to ensure data consistency
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            if (hasBinaryColumns)
                            {
                                // Create metadata for the file reference
                                string metadata = JsonConvert.SerializeObject(new { 
                                    ContentType = contentType, 
                                    UploadDate = DateTime.UtcNow,
                                    Source = "file",
                                    StoragePath = fileUrl
                                });
                                
                                // Build the SQL query based on whether OriginalResumeFileName exists
                                string query;
                                if (hasOriginalFileNameColumn)
                                {
                                    query = @"
                                        UPDATE StudentDetails 
                                        SET ResumeData = NULL,
                                            ResumeMetadata = @ResumeMetadata,
                                            ResumeFileName = @ResumeFileName,
                                            OriginalResumeFileName = @OriginalFileName
                                        WHERE IdNumber = @IdNumber";
                                }
                                else
                                {
                                    query = @"
                                        UPDATE StudentDetails 
                                        SET ResumeData = NULL,
                                            ResumeMetadata = @ResumeMetadata,
                                            ResumeFileName = @ResumeFileName
                                        WHERE IdNumber = @IdNumber";
                                }
                                
                                using (var command = new SqlCommand(query, connection, transaction))
                                {
                                    // Important: Not storing actual file data in the database
                                    command.Parameters.AddWithValue("@ResumeMetadata", metadata);
                                    command.Parameters.AddWithValue("@ResumeFileName", fileUrl);
                                    
                                    if (hasOriginalFileNameColumn)
                                    {
                                        command.Parameters.AddWithValue("@OriginalFileName", fileName);
                                    }
                                    
                                    command.Parameters.AddWithValue("@IdNumber", idNumber);
                                    rowsAffected = await command.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                // Fall back to the appropriate table
                                bool usingNewTables = false;
                                string checkTableQuery = @"
                                    SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                                    WHERE TABLE_NAME = 'StudentDetails'";
                                
                                using (var command = new SqlCommand(checkTableQuery, connection, transaction))
                                {
                                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                    usingNewTables = (count > 0);
                                }
                                
                                string query;
                                
                                if (usingNewTables)
                                {
                                    if (hasOriginalFileNameColumn)
                                    {
                                        query = @"UPDATE StudentDetails SET ResumeFileName = @ResumeUrl, OriginalResumeFileName = @OriginalFileName WHERE IdNumber = @IdNumber";
                                    }
                                    else
                                    {
                                        query = @"UPDATE StudentDetails SET ResumeFileName = @ResumeUrl WHERE IdNumber = @IdNumber";
                                    }
                                    
                                    using (var command = new SqlCommand(query, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@ResumeUrl", fileUrl);
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        
                                        if (hasOriginalFileNameColumn)
                                        {
                                            command.Parameters.AddWithValue("@OriginalFileName", fileName);
                                        }
                                        
                                        rowsAffected = await command.ExecuteNonQueryAsync();
                                    }
                                }
                                else
                                {
                                    // Old database structure
                                    query = @"UPDATE Students SET ResumeFileName = @ResumeUrl WHERE IdNumber = @IdNumber";
                                    
                                    using (var command = new SqlCommand(query, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@ResumeUrl", fileUrl);
                                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                                        rowsAffected = await command.ExecuteNonQueryAsync();
                                    }
                                }
                            }
                            
                            // Commit the transaction
                            transaction.Commit();
                            
                            if (rowsAffected > 0)
                            {
                                return Json(new
                                {
                                    success = true,
                                    message = "Resume uploaded successfully.",
                                    resumeUrl = fileUrl + $"?t={DateTime.Now.Ticks}", // Add timestamp to prevent caching
                                    fileName = fileName
                                });
                            }
                            
                            return Json(new { success = false, message = "Failed to update resume. User not found." });
                        }
                        catch (Exception ex)
                        {
                            // Rollback transaction on error
                            transaction.Rollback();
                            
                            // Try to delete the file if database update failed
                            try { System.IO.File.Delete(filePath); } catch { }
                            
                            return Json(new { success = false, message = "Database error: " + ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving resume: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("GetProfilePicture")]
        public async Task<IActionResult> GetProfilePicture(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                studentId = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(studentId))
                {
                    return NotFound();
                }
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if binary columns exist and contain data
                    bool hasBinaryData = false;
                    byte[] profileImageData = null;
                    string contentType = "image/jpeg"; // Default content type
                    
                    // First check if we have binary data in the new table structure
                    string checkBinaryDataQuery = @"
                        SELECT 
                            CASE WHEN ProfilePictureData IS NOT NULL THEN 1 ELSE 0 END AS HasBinaryData,
                            ProfilePictureData,
                            ProfileMetadata,
                            ProfilePicturePath
                        FROM StudentDetails 
                        WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(checkBinaryDataQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                hasBinaryData = Convert.ToBoolean(reader["HasBinaryData"]);
                                
                                if (hasBinaryData)
                                {
                                    // Get binary data directly
                                    profileImageData = (byte[])reader["ProfilePictureData"];
                                    
                                    // Get content type from metadata if available
                                    string metadata = reader["ProfileMetadata"] as string;
                                    if (!string.IsNullOrEmpty(metadata))
                                    {
                                        try
                                        {
                                            var metadataObj = JsonConvert.DeserializeObject<dynamic>(metadata);
                                            contentType = metadataObj.ContentType ?? "image/jpeg";
                                        }
                                        catch
                                        {
                                            // Fallback to default content type
                                        }
                                    }
                                }
                                else
                                {
                                    // If no binary data, check for profile picture path
                                    string profilePicturePath = reader["ProfilePicturePath"] as string;
                                    
                                    if (!string.IsNullOrEmpty(profilePicturePath))
                                    {
                                        // Check if path is a data URL
                                        if (profilePicturePath.StartsWith("data:"))
                                        {
                                            // Parse data URL to get binary data
                                            string[] parts = profilePicturePath.Split(',');
                                            if (parts.Length == 2)
                                            {
                                                if (parts[0].Contains(":") && parts[0].Contains(";"))
                                                {
                                                    contentType = parts[0].Split(':')[1].Split(';')[0];
                                                }
                                                
                                                try
                                                {
                                                    // Convert base64 string to binary
                                                    profileImageData = Convert.FromBase64String(parts[1]);
                                                    hasBinaryData = true;
                                                }
                                                catch
                                                {
                                                    // Invalid base64, will fall back to default image
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Physical file path, return file from wwwroot
                                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                                profilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                            
                                            if (System.IO.File.Exists(filePath))
                                            {
                                                // Set content type based on file extension
                                                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                                contentType = extension switch
                                                {
                                                    ".jpg" or ".jpeg" => "image/jpeg",
                                                    ".png" => "image/png",
                                                    ".gif" => "image/gif",
                                                    _ => "image/jpeg"
                                                };
                                                
                                                return PhysicalFile(filePath, contentType);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // If no binary data found in StudentDetails, check old Students table
                    if (!hasBinaryData)
                    {
                        // First check if the Students table exists
                        bool studentsTableExists = false;
                        string checkTableQuery = @"
                            SELECT COUNT(*) 
                            FROM INFORMATION_SCHEMA.TABLES 
                            WHERE TABLE_NAME = 'Students'";
                        
                        using (var command = new SqlCommand(checkTableQuery, connection))
                        {
                            try 
                            {
                                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                studentsTableExists = (count > 0);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error checking Students table: {ex.Message}");
                                studentsTableExists = false;
                            }
                        }
                        
                        // Only query the Students table if it exists
                        if (studentsTableExists)
                        {
                            string checkOldTableQuery = @"
                                SELECT ProfilePicturePath
                                FROM Students
                                WHERE IdNumber = @StudentId";
                            
                            using (var command = new SqlCommand(checkOldTableQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    string profilePicturePath = result.ToString();
                                    
                                    if (!string.IsNullOrEmpty(profilePicturePath))
                                    {
                                        // Check if path is a data URL
                                        if (profilePicturePath.StartsWith("data:"))
                                        {
                                            // Parse data URL to get binary data
                                            string[] parts = profilePicturePath.Split(',');
                                            if (parts.Length == 2)
                                            {
                                                if (parts[0].Contains(":") && parts[0].Contains(";"))
                                                {
                                                    contentType = parts[0].Split(':')[1].Split(';')[0];
                                                }
                                                
                                                try
                                                {
                                                    // Convert base64 string to binary
                                                    profileImageData = Convert.FromBase64String(parts[1]);
                                                    hasBinaryData = true;
                                                }
                                                catch
                                                {
                                                    // Invalid base64, will fall back to default image
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Physical file path, return file from wwwroot
                                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                                profilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                            
                                            if (System.IO.File.Exists(filePath))
                                            {
                                                // Set content type based on file extension
                                                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                                contentType = extension switch
                                                {
                                                    ".jpg" or ".jpeg" => "image/jpeg",
                                                    ".png" => "image/png",
                                                    ".gif" => "image/gif",
                                                    _ => "image/jpeg"
                                                };
                                                
                                                return PhysicalFile(filePath, contentType);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else 
                        {
                            Console.WriteLine("Students table does not exist, using default profile picture.");
                        }
                    }
                    
                    // If we have binary data, return it
                    if (hasBinaryData && profileImageData != null)
                    {
                        return File(profileImageData, contentType);
                    }
                }
                
                // The default image is only 1 byte, so it's likely corrupted
                // Return a 1x1 transparent pixel as fallback
                byte[] transparentPixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
                return File(transparentPixel, "image/gif");
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Error in GetProfilePicture: {ex.Message}");
                
                // In case of any error, return a 1x1 transparent pixel
                byte[] transparentPixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
                return File(transparentPixel, "image/gif");
            }
        }
        
        [HttpGet]
        [Route("GetResume")]
        public async Task<IActionResult> GetResume(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
            {
                studentId = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(studentId))
                {
                    return NotFound();
                }
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if binary columns exist and contain data
                    bool hasBinaryData = false;
                    byte[] resumeData = null;
                    string contentType = "application/pdf"; // Default content type
                    string originalFileName = "resume.pdf"; // Default filename
                    
                    // First check if we have binary data in the new table structure
                    string checkBinaryDataQuery = @"
                        SELECT 
                            CASE WHEN ResumeData IS NOT NULL THEN 1 ELSE 0 END AS HasBinaryData,
                            ResumeData,
                            ResumeMetadata,
                            ResumeFileName,
                            OriginalResumeFileName
                        FROM StudentDetails 
                        WHERE IdNumber = @StudentId";
                    
                    using (var command = new SqlCommand(checkBinaryDataQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                hasBinaryData = Convert.ToBoolean(reader["HasBinaryData"]);
                                
                                if (hasBinaryData)
                                {
                                    // Get binary data directly
                                    resumeData = (byte[])reader["ResumeData"];
                                    
                                    // Get content type from metadata if available
                                    string metadata = reader["ResumeMetadata"] as string;
                                    if (!string.IsNullOrEmpty(metadata))
                                    {
                                        try
                                        {
                                            var metadataObj = JsonConvert.DeserializeObject<dynamic>(metadata);
                                            contentType = metadataObj.ContentType ?? "application/pdf";
                                        }
                                        catch
                                        {
                                            // Fallback to default content type
                                        }
                                    }
                                    
                                    // Get original filename if available
                                    if (reader["OriginalResumeFileName"] != DBNull.Value)
                                    {
                                        originalFileName = reader["OriginalResumeFileName"].ToString();
                                    }
                                }
                                else
                                {
                                    // If no binary data, check for resume file path
                                    string resumeFilePath = reader["ResumeFileName"] as string;
                                    
                                    if (!string.IsNullOrEmpty(resumeFilePath))
                                    {
                                        // Check if path is a data URL
                                        if (resumeFilePath.StartsWith("data:"))
                                        {
                                            // Parse data URL to get binary data
                                            string[] parts = resumeFilePath.Split(',');
                                            if (parts.Length == 2)
                                            {
                                                if (parts[0].Contains(":") && parts[0].Contains(";"))
                                                {
                                                    contentType = parts[0].Split(':')[1].Split(';')[0];
                                                }
                                                
                                                try
                                                {
                                                    // Convert base64 string to binary
                                                    resumeData = Convert.FromBase64String(parts[1]);
                                                    hasBinaryData = true;
                                                }
                                                catch
                                                {
                                                    // Invalid base64, will fall back to not found
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Physical file path, return file from wwwroot
                                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                                resumeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                            
                                            if (System.IO.File.Exists(filePath))
                                            {
                                                // Set content type based on file extension
                                                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                                contentType = extension switch
                                                {
                                                    ".pdf" => "application/pdf",
                                                    ".doc" => "application/msword",
                                                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                                    _ => "application/octet-stream"
                                                };
                                                
                                                // Set original filename based on the last part of the path
                                                originalFileName = Path.GetFileName(filePath);
                                                
                                                // Return the file with display in browser
                                                return PhysicalFile(filePath, contentType);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // If no binary data found in StudentDetails, check old Students table
                    if (!hasBinaryData)
                    {
                        // First check if the Students table exists
                        bool studentsTableExists = false;
                        string checkTableQuery = @"
                            SELECT COUNT(*) 
                            FROM INFORMATION_SCHEMA.TABLES 
                            WHERE TABLE_NAME = 'Students'";
                        
                        using (var command = new SqlCommand(checkTableQuery, connection))
                        {
                            try 
                            {
                                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                                studentsTableExists = (count > 0);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error checking Students table: {ex.Message}");
                                studentsTableExists = false;
                            }
                        }
                        
                        // Only query the Students table if it exists
                        if (studentsTableExists)
                        {
                            string checkOldTableQuery = @"
                                SELECT ResumeFileName
                                FROM Students
                                WHERE IdNumber = @StudentId";
                            
                            using (var command = new SqlCommand(checkOldTableQuery, connection))
                            {
                                command.Parameters.AddWithValue("@StudentId", studentId);
                                
                                var result = await command.ExecuteScalarAsync();
                                if (result != null && result != DBNull.Value)
                                {
                                    string resumeFilePath = result.ToString();
                                    
                                    if (!string.IsNullOrEmpty(resumeFilePath))
                                    {
                                        // Check if path is a data URL
                                        if (resumeFilePath.StartsWith("data:"))
                                        {
                                            // Parse data URL to get binary data
                                            string[] parts = resumeFilePath.Split(',');
                                            if (parts.Length == 2)
                                            {
                                                if (parts[0].Contains(":") && parts[0].Contains(";"))
                                                {
                                                    contentType = parts[0].Split(':')[1].Split(';')[0];
                                                }
                                                
                                                try
                                                {
                                                    // Convert base64 string to binary
                                                    resumeData = Convert.FromBase64String(parts[1]);
                                                    hasBinaryData = true;
                                                }
                                                catch
                                                {
                                                    // Invalid base64, will fall back to not found
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Physical file path, return file from wwwroot
                                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                                resumeFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                                            
                                            if (System.IO.File.Exists(filePath))
                                            {
                                                // Set content type based on file extension
                                                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                                                contentType = extension switch
                                                {
                                                    ".pdf" => "application/pdf",
                                                    ".doc" => "application/msword",
                                                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                                    _ => "application/octet-stream"
                                                };
                                                
                                                // Set original filename based on the last part of the path
                                                originalFileName = Path.GetFileName(filePath);
                                                
                                                // Return the file with display in browser
                                                return PhysicalFile(filePath, contentType);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else 
                        {
                            Console.WriteLine("Students table does not exist, using default resume behavior.");
                        }
                    }
                    
                    // If we have binary data, return it as a downloadable file
                    if (hasBinaryData && resumeData != null)
                    {
                        return File(resumeData, contentType, originalFileName);
                    }
                }
                
                // If we get here, no valid resume was found
                return NotFound("Resume not found.");
            }
            catch (Exception ex)
            {
                return BadRequest("Error retrieving resume: " + ex.Message);
            }
        }

        [HttpPost]
        [Route("UploadProfilePicture")]
        public async Task<IActionResult> UploadProfilePicture(IFormFile profilePicture)
        {
            if (profilePicture == null || profilePicture.Length == 0)
            {
                return Json(new { success = false, message = "No file uploaded" });
            }

            try
            {
                string idNumber = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(idNumber))
                {
                    return Json(new { success = false, message = "User not authenticated." });
                }

                // Define the directory path
                string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
                
                // Ensure directory exists
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }
                
                // Get file extension
                string extension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();
                
                // Validate extension
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".gif")
                {
                    return Json(new { success = false, message = "Invalid file format. Please upload a JPG, PNG, or GIF file." });
                }
                
                // Create a unique filename
                string fileName = $"{idNumber}_{DateTime.Now.Ticks}{extension}";
                string filePath = Path.Combine(uploadsDir, fileName);
                
                // Save the file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(stream);
                }
                
                // URL to access the file
                string fileUrl = $"/uploads/profiles/{fileName}";
                
                // Update the database
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if we should use StudentDetails or Students table
                    bool useStudentDetails = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'StudentDetails'";
                    
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        useStudentDetails = (count > 0);
                    }
                    
                    string query;
                    
                    if (useStudentDetails)
                    {
                        // Update StudentDetails table in the new structure
                        query = @"
                            UPDATE StudentDetails 
                            SET ProfilePicturePath = @ProfilePicturePath
                            WHERE IdNumber = @IdNumber";
                    }
                    else
                    {
                        // Update Students table in the old structure
                        query = @"
                            UPDATE Students 
                            SET ProfilePicturePath = @ProfilePicturePath
                            WHERE IdNumber = @IdNumber";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        // Store just the file path
                        command.Parameters.AddWithValue("@ProfilePicturePath", fileUrl);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new
                            {
                                success = true,
                                message = "Profile picture uploaded successfully.",
                                imagePath = fileUrl
                            });
                        }
                        else
                        {
                            // Try to delete the file if database update failed
                            try { System.IO.File.Delete(filePath); } catch { }
                            return Json(new { success = false, message = "Failed to update profile picture. User not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error uploading profile picture: " + ex.Message });
            }
        }

        [HttpPost]
        [Route("UploadResume")]
        public async Task<IActionResult> UploadResume(IFormFile resume)
        {
            if (resume == null || resume.Length == 0)
            {
                return Json(new { success = false, message = "No file uploaded" });
            }

            try
            {
                string idNumber = HttpContext.Session.GetString("IdNumber");
                if (string.IsNullOrEmpty(idNumber))
                {
                    return Json(new { success = false, message = "User not authenticated." });
                }

                // Define the directory path
                string uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "resumes");
                
                // Ensure directory exists
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }
                
                // Get file extension
                string extension = Path.GetExtension(resume.FileName).ToLowerInvariant();
                
                // Validate extension
                if (extension != ".pdf" && extension != ".doc" && extension != ".docx")
                {
                    return Json(new { success = false, message = "Invalid file format. Please upload a PDF or Word document." });
                }
                
                // Create a unique filename
                string fileName = $"{idNumber}_{DateTime.Now.Ticks}{extension}";
                string filePath = Path.Combine(uploadsDir, fileName);
                
                // Save the file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await resume.CopyToAsync(stream);
                }
                
                // URL to access the file
                string fileUrl = $"/uploads/resumes/{fileName}";
                
                // Update the database
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Check if we should use StudentDetails or Students table
                    bool useStudentDetails = false;
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'StudentDetails'";
                    
                    using (var command = new SqlCommand(checkTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                        useStudentDetails = (count > 0);
                    }
                    
                    // Check if OriginalResumeFileName column exists
                    bool hasOriginalFileNameColumn = false;
                    if (useStudentDetails)
                    {
                        string checkOriginalFileNameColumnQuery = @"
                            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                            WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'OriginalResumeFileName'";
                        
                        using (var command = new SqlCommand(checkOriginalFileNameColumnQuery, connection))
                        {
                            int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                            hasOriginalFileNameColumn = (count > 0);
                        }
                    }
                    
                    string query;
                    
                    if (useStudentDetails)
                    {
                        if (hasOriginalFileNameColumn)
                        {
                            // Update StudentDetails table with original filename
                            query = @"
                                UPDATE StudentDetails 
                                SET ResumeFileName = @ResumeFileName, OriginalResumeFileName = @OriginalResumeFileName
                                WHERE IdNumber = @IdNumber";
                        }
                        else
                        {
                            // Update StudentDetails table without original filename
                            query = @"
                                UPDATE StudentDetails 
                                SET ResumeFileName = @ResumeFileName
                                WHERE IdNumber = @IdNumber";
                        }
                    }
                    else
                    {
                        // Update Students table in the old structure
                        query = @"
                            UPDATE Students 
                            SET ResumeFileName = @ResumeFileName
                            WHERE IdNumber = @IdNumber";
                    }

                    using (var command = new SqlCommand(query, connection))
                    {
                        // Store the file path
                        command.Parameters.AddWithValue("@ResumeFileName", fileUrl);
                        command.Parameters.AddWithValue("@IdNumber", idNumber);
                        
                        if (hasOriginalFileNameColumn)
                        {
                            command.Parameters.AddWithValue("@OriginalResumeFileName", resume.FileName);
                        }
                        
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        
                        if (rowsAffected > 0)
                        {
                            return Json(new
                            {
                                success = true,
                                message = "Resume uploaded successfully.",
                                resumeUrl = fileUrl,
                                fileName = resume.FileName
                            });
                        }
                        else
                        {
                            // Try to delete the file if database update failed
                            try { System.IO.File.Delete(filePath); } catch { }
                            return Json(new { success = false, message = "Failed to update resume. User not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error uploading resume: " + ex.Message });
            }
        }

        private bool IsValidBase64(string base64String)
        {
            if (string.IsNullOrEmpty(base64String))
                return false;

            // Check that the string contains only valid base64 characters
            Span<char> buffer = stackalloc char[base64String.Length];
            base64String.CopyTo(buffer);

            // Try to convert from base64
            try
            {
                Convert.TryFromBase64Chars(buffer, new Span<byte>(), out int _);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
