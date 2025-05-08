using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using StudentBadge.Models;

namespace StudentBadge.Hubs
{
    public class VideoCallHub : Hub
    {
        private readonly string _connectionString;
        private static Dictionary<string, string> _userConnections = new Dictionary<string, string>();
        
        public VideoCallHub(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task RegisterConnection(string userId, string userType)
        {
            // Register the user connection
            if (!_userConnections.ContainsKey(userId))
            {
                _userConnections.Add(userId, Context.ConnectionId);
            }
            else
            {
                _userConnections[userId] = Context.ConnectionId;
            }
            
            await Clients.Caller.SendAsync("ConnectionRegistered", userId);
        }

        public async Task RequestCall(string employerId, string studentId)
        {
            try
            {
                // Only employers can initiate calls
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // First check if the student is available for chat (this always exists)
                    string chatAvailabilityQuery = @"
                        SELECT ISNULL(IsChatAvailable, 0) AS IsChatAvailable
                        FROM StudentDetails
                        WHERE IdNumber = @StudentId";
                    
                    bool isChatAvailable = false;
                    
                    using (var availabilityCommand = new SqlCommand(chatAvailabilityQuery, connection))
                    {
                        availabilityCommand.Parameters.AddWithValue("@StudentId", studentId);
                        using (var reader = await availabilityCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                isChatAvailable = Convert.ToBoolean(reader["IsChatAvailable"]);
                            }
                        }
                    }
                    
                    // Don't allow video calls if chat is disabled
                    if (!isChatAvailable)
                    {
                        await Clients.Caller.SendAsync("Error", "The student is not available for video calls at this time.");
                        return;
                    }
                    
                    // Now check if video call column exists and is enabled (if column exists)
                    bool isVideoCallAvailable = false;
                    
                    // First check if the column exists
                    bool videoCallColumnExists = false;
                    string checkColumnQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = 'StudentDetails' AND COLUMN_NAME = 'IsVideoCallAvailable'";
                    
                    using (var checkCommand = new SqlCommand(checkColumnQuery, connection))
                    {
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        videoCallColumnExists = (count > 0);
                    }
                    
                    // If column exists, check its value
                    if (videoCallColumnExists)
                    {
                        string videoCallQuery = @"
                            SELECT ISNULL(IsVideoCallAvailable, 0) AS IsVideoCallAvailable
                            FROM StudentDetails
                            WHERE IdNumber = @StudentId";
                        
                        using (var videoCallCommand = new SqlCommand(videoCallQuery, connection))
                        {
                            videoCallCommand.Parameters.AddWithValue("@StudentId", studentId);
                            using (var reader = await videoCallCommand.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    isVideoCallAvailable = Convert.ToBoolean(reader["IsVideoCallAvailable"]);
                                }
                            }
                        }
                        
                        // Don't allow video calls if they're explicitly disabled
                        if (!isVideoCallAvailable)
                        {
                            await Clients.Caller.SendAsync("Error", "The student is not accepting video calls at this time.");
                            return;
                        }
                    }
                    
                    // First verify the student exists and get their UserId
                    string getUserIdQuery = @"
                        SELECT u.UserId 
                        FROM Users u
                        JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE sd.IdNumber = @StudentId AND u.Role = 'student'";
                    
                    string studentUserId = null;
                    using (var userIdCommand = new SqlCommand(getUserIdQuery, connection))
                    {
                        userIdCommand.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await userIdCommand.ExecuteScalarAsync();
                        
                        if (result == null)
                        {
                            await Clients.Caller.SendAsync("Error", "Student not found");
                            return;
                        }
                        
                        studentUserId = result.ToString();
                    }

                    // Check if we're using the old Employers table or the new EmployerDetails schema
                    bool usingOldSchema = false;
                    string checkEmployersTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'Employers'";
                        
                    using (var checkCommand = new SqlCommand(checkEmployersTableQuery, connection))
                    {
                        int count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        usingOldSchema = (count > 0);
                    }
                    
                    // Handle the case where employerId is passed as "@jsEmployerId" from the JavaScript
                    // by extracting the real employer ID from the connection context
                    string actualEmployerId = employerId;
                    if (employerId == "@jsEmployerId" || string.IsNullOrEmpty(employerId))
                    {
                        // Get the connection ID from context and look up the user that this connection belongs to
                        string connectionId = Context.ConnectionId;
                        
                        // Find the employer ID associated with this connection
                        foreach (var kvp in _userConnections)
                        {
                            if (kvp.Value == connectionId)
                            {
                                actualEmployerId = kvp.Key;
                                break;
                            }
                        }
                        
                        if (string.IsNullOrEmpty(actualEmployerId) || actualEmployerId == "@jsEmployerId")
                        {
                            await Clients.Caller.SendAsync("Error", "Could not determine your employer ID. Please try refreshing the page.");
                            return;
                        }
                    }
                    
                    // For the new schema, we need to verify the employer exists in Users table
                    if (!usingOldSchema)
                    {
                        string checkEmployerQuery = @"
                            SELECT UserId FROM Users 
                            WHERE UserId = @EmployerId AND Role = 'employer'";
                            
                        using (var checkCommand = new SqlCommand(checkEmployerQuery, connection))
                        {
                            checkCommand.Parameters.AddWithValue("@EmployerId", actualEmployerId);
                            var result = await checkCommand.ExecuteScalarAsync();
                            
                            if (result == null)
                            {
                                await Clients.Caller.SendAsync("Error", "Employer not found in the Users table. Please contact support.");
                                return;
                            }
                        }
                    }
                    
                    // Now insert the video call request using the appropriate IDs
                    var query = @"
                        INSERT INTO VideoCalls (EmployerId, StudentId, Status)
                        VALUES (@EmployerId, @StudentId, 'requested');
                        SELECT SCOPE_IDENTITY();";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", actualEmployerId);
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        var callId = Convert.ToInt32(await command.ExecuteScalarAsync());

                        // Notify the student about the call request
                        if (_userConnections.TryGetValue(studentId, out string studentConnectionId))
                        {
                            await Clients.Client(studentConnectionId).SendAsync("IncomingCall", callId, actualEmployerId);
                        }

                        // Notify the employer that the request was sent
                        await Clients.Caller.SendAsync("CallRequested", callId);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Error requesting call: {ex.Message}");
            }
        }

        public async Task RespondToCall(int callId, string studentId, string response)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the student's UserId from their IdNumber
                    string getUserIdQuery = @"
                        SELECT u.UserId 
                        FROM Users u
                        JOIN StudentDetails sd ON u.UserId = sd.UserId
                        WHERE sd.IdNumber = @StudentId AND u.Role = 'student'";
                    
                    string studentUserId = null;
                    using (var userIdCommand = new SqlCommand(getUserIdQuery, connection))
                    {
                        userIdCommand.Parameters.AddWithValue("@StudentId", studentId);
                        var result = await userIdCommand.ExecuteScalarAsync();
                        
                        if (result == null)
                        {
                            await Clients.Caller.SendAsync("Error", "Student not found");
                            return;
                        }
                        
                        studentUserId = result.ToString();
                    }
                    
                    // First get the call information
                    string getCallQuery = "SELECT EmployerId FROM VideoCalls WHERE CallId = @CallId AND StudentId = @StudentId";
                    string employerId = null;
                    
                    using (var command = new SqlCommand(getCallQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        command.Parameters.AddWithValue("@StudentId", studentUserId);
                        employerId = (string)await command.ExecuteScalarAsync();
                    }
                    
                    if (string.IsNullOrEmpty(employerId))
                    {
                        await Clients.Caller.SendAsync("Error", "Call not found");
                        return;
                    }
                    
                    // Update call status
                    string status = response == "accept" ? "accepted" : "declined";
                    string updateQuery = "UPDATE VideoCalls SET Status = @Status WHERE CallId = @CallId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Status", status);
                        command.Parameters.AddWithValue("@CallId", callId);
                        await command.ExecuteNonQueryAsync();
                    }

                    // Notify the employer
                    if (_userConnections.TryGetValue(employerId, out string employerConnectionId))
                    {
                        await Clients.Client(employerConnectionId).SendAsync("CallResponse", callId, status);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Error responding to call: {ex.Message}");
            }
        }

        public async Task SendSignal(int callId, string signal, string userId, string userType)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the call participants
                    string query = "SELECT EmployerId, StudentId FROM VideoCalls WHERE CallId = @CallId";
                    string employerId = null;
                    string studentUserId = null;
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                employerId = reader["EmployerId"].ToString();
                                studentUserId = reader["StudentId"].ToString();
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(employerId) || string.IsNullOrEmpty(studentUserId))
                    {
                        await Clients.Caller.SendAsync("Error", "Call not found");
                        return;
                    }
                    
                    // Get the student's IdNumber for SignalR connection lookup
                    string studentIdNumber = null;
                    if (userType == "employer")
                    {
                        string getStudentQuery = @"
                            SELECT IdNumber
                            FROM StudentDetails
                            WHERE UserId = @StudentUserId";
                        
                        using (var command = new SqlCommand(getStudentQuery, connection))
                        {
                            command.Parameters.AddWithValue("@StudentUserId", studentUserId);
                            studentIdNumber = (string)await command.ExecuteScalarAsync();
                        }
                    }
                    
                    // Determine recipient based on sender
                    string recipientId = null;
                    if (userType == "employer")
                    {
                        recipientId = studentIdNumber; // Use IdNumber for student
                    }
                    else
                    {
                        recipientId = employerId; // Employer ID is already the UserId
                    }
                    
                    // Forward the signal to the recipient
                    if (_userConnections.TryGetValue(recipientId, out string recipientConnectionId))
                    {
                        await Clients.Client(recipientConnectionId).SendAsync("ReceiveSignal", callId, signal, userId, userType);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Error sending signal: {ex.Message}");
            }
        }

        public async Task EndCall(int callId, string userId, string userType)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    
                    // Get the call participants
                    string query = "SELECT EmployerId, StudentId FROM VideoCalls WHERE CallId = @CallId";
                    string employerId = null;
                    string studentUserId = null;
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                employerId = reader["EmployerId"].ToString();
                                studentUserId = reader["StudentId"].ToString();
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(employerId) || string.IsNullOrEmpty(studentUserId))
                    {
                        await Clients.Caller.SendAsync("Error", "Call not found");
                        return;
                    }
                    
                    // Get the student's IdNumber for SignalR connection lookup
                    string studentIdNumber = null;
                    string getStudentQuery = @"
                        SELECT IdNumber
                        FROM StudentDetails
                        WHERE UserId = @StudentUserId";
                    
                    using (var command = new SqlCommand(getStudentQuery, connection))
                    {
                        command.Parameters.AddWithValue("@StudentUserId", studentUserId);
                        studentIdNumber = await command.ExecuteScalarAsync() as string;
                    }
                    
                    // Update call status and end time
                    string updateQuery = "UPDATE VideoCalls SET Status = 'completed', EndTime = GETDATE() WHERE CallId = @CallId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    // Notify the other participant
                    string otherParticipantId = null;
                    if (userType == "employer")
                    {
                        otherParticipantId = studentIdNumber; // Use IdNumber for student
                    }
                    else
                    {
                        otherParticipantId = employerId; // Employer ID is already UserId
                    }

                    if (_userConnections.TryGetValue(otherParticipantId, out string connectionId))
                    {
                        await Clients.Client(connectionId).SendAsync("CallEnded", callId);
                    }
                }
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Error ending call: {ex.Message}");
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Remove user from connections dictionary
            string userId = _userConnections.FirstOrDefault(x => x.Value == Context.ConnectionId).Key;
            if (!string.IsNullOrEmpty(userId))
            {
                _userConnections.Remove(userId);
            }
            
            await base.OnDisconnectedAsync(exception);
        }
    }
} 