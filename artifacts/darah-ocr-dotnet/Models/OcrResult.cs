using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DarahOcr.Models;

[Table("dotnet_ocr_results")]
public class OcrResult
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int JobId { get; set; }

    public string? RawText { get; set; }

    public string? RefinedText { get; set; }

    public double ConfidenceScore { get; set; }

    [MaxLength(20)]
    public string QualityLevel { get; set; } = "medium";

    public int WordCount { get; set; }

    public int PassCount { get; set; } = 1;

    public int PageCount { get; set; } = 1;

    public string? ProcessingNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(JobId))]
    public Job? Job { get; set; }
}
