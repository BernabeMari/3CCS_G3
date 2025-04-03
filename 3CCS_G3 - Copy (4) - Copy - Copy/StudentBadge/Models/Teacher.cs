namespace StudentBadge.Models
{
    public class Teacher
    {
        public string TeacherId { get; set; }
        public string FullName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Department { get; set; }
        public string Position { get; set; }
        
        // Store profile picture directly in the database
        public byte[] ProfilePictureData { get; set; }
        public string ProfilePictureContentType { get; set; }
    }
} 