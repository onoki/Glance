using DocumentFormat.OpenXml.Packaging;
using Glance.Server.StatusUpdates;

namespace Glance.Server.Tests;

public sealed class StatusOfficeExporterTests
{
    private static StatusExportDocument Sample() => new(
        "2026-08-01",
        "Project",
        new StatusPeriod(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-28)),
        new StatusOutput(DateTimeOffset.UtcNow, "On track", new[] {
            new StatusRisk("r1", "Schedule", "Review delivery date", true, Array.Empty<StatusSourceReference>())
        }, new[] {
            new StatusQuestion("q1", "Who approves?", false, Array.Empty<StatusSourceReference>())
        }));

    [Fact]
    public void Excel_HasOneWorksheet()
    {
        var bytes = new StatusOfficeExporter().CreateExcel(Sample());
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var sheets = document.WorkbookPart?.Workbook?.Sheets;
        Assert.NotNull(sheets);
        Assert.Single(sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>());
    }

    [Fact]
    public void PowerPoint_HasExactlyOneSlide()
    {
        var bytes = new StatusOfficeExporter().CreatePowerPoint(Sample());
        using var stream = new MemoryStream(bytes);
        using var document = PresentationDocument.Open(stream, false);
        Assert.Single(document.PresentationPart?.SlideParts ?? []);
    }
}
