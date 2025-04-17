using System;

namespace StudentBadge.Models
{
    public class ExtraCurricularActivity
    {
        public int ActivityId { get; set; }
        public string StudentId { get; set; }
        public string TeacherId { get; set; }
        public string ActivityName { get; set; }
        public string ActivityDescription { get; set; }
        public string ActivityCategory { get; set; }
        public DateTime ActivityDate { get; set; }
        public DateTime RecordedDate { get; set; }
        public decimal Score { get; set; }
        public byte[] ProofImageData { get; set; }
        public string ProofImageContentType { get; set; }
        public bool IsVerified { get; set; }
    }
} 