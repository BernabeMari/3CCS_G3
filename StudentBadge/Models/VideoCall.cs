using System;

namespace StudentBadge.Models
{
    public class VideoCall
    {
        public int CallId { get; set; }
        public string EmployerId { get; set; }
        public string StudentId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } // 'requested', 'accepted', 'declined', 'completed', 'missed'
    }

    // View model classes for video calls
    public class VideoCallRequestModel
    {
        public string StudentId { get; set; }
    }

    public class VideoCallResponseModel
    {
        public string CallId { get; set; }
        public string Status { get; set; }
    }

    public class VideoCallSignalModel
    {
        public string CallId { get; set; }
        public string Signal { get; set; }
        public string Type { get; set; }
        public string UserId { get; set; }
        public string UserType { get; set; } // 'employer' or 'student'
    }
} 