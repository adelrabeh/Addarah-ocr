using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DarahOcr.Services;

public class DocxService
{
    public byte[] GenerateDocx(string title, string text, double confidence, string quality, DateTime processedAt)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            // Title
            AddParagraph(body, title, bold: true, fontSize: 32, justify: JustificationValues.Center);

            // Metadata
            AddParagraph(body, $"تاريخ المعالجة: {processedAt:yyyy/MM/dd}", bold: false, fontSize: 20, justify: JustificationValues.Right);
            AddParagraph(body, $"نسبة الثقة: {confidence}% | الجودة: {quality}", bold: false, fontSize: 20, justify: JustificationValues.Right);
            AddParagraph(body, "", bold: false);

            // Divider
            AddParagraph(body, "─────────────────────────────────────", bold: false, fontSize: 18, justify: JustificationValues.Center);
            AddParagraph(body, "", bold: false);

            // OCR Text header
            AddParagraph(body, "النص المستخرج:", bold: true, fontSize: 24, justify: JustificationValues.Right);
            AddParagraph(body, "", bold: false);

            // OCR Text
            foreach (var line in text.Split('\n'))
            {
                AddParagraph(body, line.Trim(), bold: false, fontSize: 24, justify: JustificationValues.Right);
            }

            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static void AddParagraph(
        Body body,
        string text,
        bool bold,
        int fontSize = 24,
        JustificationValues? justify = null)
    {
        var para = body.AppendChild(new Paragraph());

        var resolvedJustify = justify ?? JustificationValues.Right;
        var pPr = new ParagraphProperties(
            new Justification { Val = resolvedJustify },
            new BiDi()
        );
        para.AppendChild(pPr);

        if (!string.IsNullOrEmpty(text))
        {
            var run = para.AppendChild(new Run());
            var rPr = new RunProperties(
                new RunFonts { Ascii = "Arial", HighAnsi = "Arial", ComplexScript = "Arial" },
                new FontSize { Val = fontSize.ToString() },
                new FontSizeComplexScript { Val = fontSize.ToString() },
                new RightToLeftText()
            );
            if (bold) rPr.PrependChild(new Bold());
            run.AppendChild(rPr);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }
    }
}
