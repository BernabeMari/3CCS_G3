using System;

namespace StudentBadge.Models
{
    public class Certificate
    {
        public int CertificateId { get; set; }
        
        public string StudentId { get; set; }
        
        public string StudentName { get; set; }
        
        public int TestId { get; set; }
        
        public string TestName { get; set; }
        
        public string ProgrammingLanguage { get; set; }
        
        public int GradeLevel { get; set; }
        
        public int Score { get; set; }
        
        public DateTime IssueDate { get; set; }
        
        // Certificate content can be stored as HTML or any other format
        public string CertificateContent { get; set; }
        
        // For storing generated certificate as PDF or image
        public byte[] CertificateData { get; set; }
        
        public string CertificateContentType { get; set; }
    }
} 