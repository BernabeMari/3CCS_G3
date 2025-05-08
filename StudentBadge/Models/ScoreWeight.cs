using System;
using System.ComponentModel.DataAnnotations;

namespace StudentBadge.Models
{
    public class ScoreWeight
    {
        public int Id { get; set; }
        
        [Required]
        [Display(Name = "Category Name")]
        public string CategoryName { get; set; }
        
        [Required]
        [Display(Name = "Weight (%)")]
        [Range(0, 100, ErrorMessage = "Weight must be between 0 and 100")]
        public decimal Weight { get; set; }
        
        [Display(Name = "Description")]
        public string Description { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime ModifiedDate { get; set; }
    }
} 