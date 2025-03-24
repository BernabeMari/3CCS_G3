using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;
using System.Data.SqlClient;
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
            _connectionString = configuration.GetConnectionString("YourConnectionString");
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
                    var query = @"
                        INSERT INTO VideoCalls (EmployerId, StudentId, Status)
                        VALUES (@EmployerId, @StudentId, 'requested');
                        SELECT SCOPE_IDENTITY();";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@EmployerId", employerId);
                        command.Parameters.AddWithValue("@StudentId", studentId);
                        var callId = Convert.ToInt32(await command.ExecuteScalarAsync());

                        // Notify the student about the call request
                        if (_userConnections.TryGetValue(studentId, out string studentConnectionId))
                        {
                            await Clients.Client(studentConnectionId).SendAsync("IncomingCall", callId, employerId);
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
                    
                    // First get the call information
                    string getCallQuery = "SELECT EmployerId FROM VideoCalls WHERE CallId = @CallId";
                    string employerId = null;
                    
                    using (var command = new SqlCommand(getCallQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
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
                    
                    // Get the other participant
                    string query = "SELECT EmployerId, StudentId FROM VideoCalls WHERE CallId = @CallId";
                    string employerId = null;
                    string studentId = null;
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                employerId = reader["EmployerId"].ToString();
                                studentId = reader["StudentId"].ToString();
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(employerId) || string.IsNullOrEmpty(studentId))
                    {
                        await Clients.Caller.SendAsync("Error", "Call not found");
                        return;
                    }
                    
                    // Determine recipient based on sender
                    string recipientId = userType == "employer" ? studentId : employerId;
                    
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
                    string studentId = null;
                    
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                employerId = reader["EmployerId"].ToString();
                                studentId = reader["StudentId"].ToString();
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(employerId) || string.IsNullOrEmpty(studentId))
                    {
                        await Clients.Caller.SendAsync("Error", "Call not found");
                        return;
                    }
                    
                    // Update call status and end time
                    string updateQuery = "UPDATE VideoCalls SET Status = 'completed', EndTime = GETDATE() WHERE CallId = @CallId";
                    
                    using (var command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@CallId", callId);
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    // Notify the other participant
                    string otherParticipantId = userType == "employer" ? studentId : employerId;
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