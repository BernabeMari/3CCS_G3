using System;

namespace StudentBadge.Models
{
    public class AttendanceRecord
    {
        public int AttendanceId { get; set; }
        public string StudentId { get; set; }
        public string TeacherId { get; set; }
        public string EventName { get; set; }
        public string EventDescription { get; set; }
        public DateTime EventDate { get; set; }
        public DateTime RecordedDate { get; set; }
        public decimal Score { get; set; }
        public byte[] ProofImageData { get; set; }
        public string ProofImageContentType { get; set; }
        public bool IsVerified { get; set; }
    }
} 