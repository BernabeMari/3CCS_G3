using System.Collections.Generic;

namespace StudentBadge.Models
{
    public class SubmitTestViewModel
    {
        public int TestId { get; set; }
        public string StudentId { get; set; }
        public List<TestAnswerViewModel> Answers { get; set; }
    }
    
    public class TestAnswerViewModel
    {
        public int QuestionId { get; set; }
        public string Answer { get; set; }
    }
} 