using Microsoft.EntityFrameworkCore;
using StudentBadge.Models;

namespace StudentBadge.Data
{
    public class StudentContext : DbContext
    {
        public StudentContext(DbContextOptions<StudentContext> options) : base(options) { }

        public DbSet<Student> Students { get; set; }

    }
}