using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DarahOcr.Models;

[Table("dotnet_users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Username { get; set; } = "";

    [Required, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required]
    public string PasswordHash { get; set; } = "";

    [MaxLength(20)]
    public string Role { get; set; } = "user"; // "admin" | "user"

    public string[] Permissions { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public int FailedLoginAttempts { get; set; } = 0;

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Job> Jobs { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
