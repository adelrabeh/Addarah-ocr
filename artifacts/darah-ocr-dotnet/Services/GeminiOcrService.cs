using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DarahOcr.Services;

public class GeminiOcrService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<GeminiOcrService> logger)
{
    private static readonly string[] PdfExtensions = [".pdf"];

    public async Task<OcrPageResult> ProcessFileAsync(
        string filePath,
        string fileType,
        IProgress<(int page, int total)>? progress = null,
        CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var allText = new StringBuilder();
        int pageCount = 1;

        if (PdfExtensions.Contains(ext))
        {
            var images = await ConvertPdfToImagesAsync(filePath, ct);
            pageCount = images.Count;

            for (int i = 0; i < images.Count; i++)
            {
                progress?.Report((i + 1, pageCount));
                var pageText = await CallGeminiAsync(images[i], "image/jpeg", ct);
                allText.AppendLine(pageText);
                if (i < images.Count - 1) await Task.Delay(500, ct);

                foreach (var img in images) { try { File.Delete(img); } catch { } }
            }
        }
        else
        {
            progress?.Report((1, 1));
            var mimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".tif" or ".tiff" => "image/tiff",
                _ => "image/jpeg"
            };
            allText.Append(await CallGeminiAsync(filePath, mimeType, ct));
        }

        var rawText = allText.ToString().Trim();
        var (confidence, refinedText) = PostProcess(rawText);

        return new OcrPageResult
        {
            RawText = rawText,
            RefinedText = refinedText,
            ConfidenceScore = confidence,
            QualityLevel = confidence >= 85 ? "high" : confidence >= 65 ? "medium" : "low",
            WordCount = refinedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            PageCount = pageCount
        };
    }

    private async Task<string> CallGeminiAsync(string imagePath, string mimeType, CancellationToken ct)
    {
        var apiKey = config["Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("AI_INTEGRATIONS_GEMINI_API_KEY")
            ?? throw new InvalidOperationException("Gemini API key not configured");

        var baseUrl = Environment.GetEnvironmentVariable("AI_INTEGRATIONS_GEMINI_BASE_URL")
            ?? "https://generativelanguage.googleapis.com";

        var model = config["Gemini:Model"] ?? "gemini-2.0-flash";

        var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
        var base64 = Convert.ToBase64String(imageBytes);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inlineData = new { mimeType, data = base64 }
                        },
                        new
                        {
                            text = "استخرج النص العربي من هذه الوثيقة بدقة. حافظ على التنسيق الأصلي والفقرات. علّم الكلمات غير المقروءة بـ [كلمة] والكلمات المشكوك فيها بـ [؟] بعدها مباشرة. أعد النص المستخرج فقط بدون شرح."
                        }
                    }
                }
            },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 8192 }
        };

        var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(3);

        var url = $"{baseUrl.TrimEnd('/')}/v1beta/models/{model}:generateContent?key={apiKey}";
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await http.PostAsync(url, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Gemini API error: {Status} {Body}", response.StatusCode, responseJson);
            throw new Exception($"Gemini API error: {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return text;
    }

    private static async Task<List<string>> ConvertPdfToImagesAsync(string pdfPath, CancellationToken ct)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outputDir);
        var outputPrefix = Path.Combine(outputDir, "page");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pdftoppm",
            Arguments = $"-r 200 -jpeg \"{pdfPath}\" \"{outputPrefix}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc != null) await proc.WaitForExitAsync(ct);

        var images = Directory.GetFiles(outputDir, "*.jpg")
            .OrderBy(f => f)
            .ToList();

        return images;
    }

    private static (double confidence, string refined) PostProcess(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return (0, "");

        var unreadable = Regex.Matches(rawText, @"\[كلمة\]").Count;
        var uncertain = Regex.Matches(rawText, @"\[؟\]").Count;
        var totalWords = rawText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var confidence = totalWords == 0 ? 0.0 :
            Math.Max(0, 100.0 - (unreadable * 5) - (uncertain * 2));
        confidence = Math.Round(confidence, 1);

        var refined = rawText.Trim();
        return (confidence, refined);
    }
}

public class OcrPageResult
{
    public string RawText { get; set; } = "";
    public string RefinedText { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public string QualityLevel { get; set; } = "medium";
    public int WordCount { get; set; }
    public int PageCount { get; set; } = 1;
}
