using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DarahOcr.Models;

[Table("dotnet_jobs")]
public class Job
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? ProjectId { get; set; }

    [Required, MaxLength(512)]
    public string Filename { get; set; } = "";

    [Required, MaxLength(512)]
    public string OriginalFilename { get; set; } = "";

    [MaxLength(10)]
    public string FileType { get; set; } = "";

    public long FileSize { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "pending";
    // pending | processing | ocr_complete | reviewed | approved | rejected | failed

    public int RetryCount { get; set; } = 0;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public int? ProcessingPage { get; set; }
    public int? TotalPages { get; set; }
    public long? ProcessingDurationMs { get; set; }

    public int? ReviewerId { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public int? ApproverId { get; set; }
    public string? ApproveNotes { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public OcrResult? OcrResult { get; set; }
}
