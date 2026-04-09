using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DarahOcr.Models;

[Table("dotnet_api_keys")]
public class ApiKey
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [Required]
    public string KeyHash { get; set; } = "";

    [Required, MaxLength(20)]
    public string Prefix { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
