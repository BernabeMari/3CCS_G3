using System;

namespace StudentBadge.Models
{
    public class StudentCertificate
    {
        public int CertificateId { get; set; }
        public string StudentId { get; set; }
        public string CertificateType { get; set; } // "seminar" or "extracurricular"
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime UploadDate { get; set; }
        public byte[] CertificateData { get; set; }
        public string FileName { get; set; }
        public bool IsVerified { get; set; }
        public string VerifiedBy { get; set; }
        public DateTime? VerificationDate { get; set; }
    }
    
    public class StudentCertificateViewModel : StudentCertificate
    {
        public string StudentName { get; set; }
    }
} 