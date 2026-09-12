using System.IO.Compression;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Glance.Server.StatusUpdates;

public sealed class StatusOfficeExporter
{
    public byte[] CreateExcel(StatusExportDocument document)
    {
        using var stream = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = spreadsheet.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStyles();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(
                new Columns(
                    new Column { Min = 1, Max = 1, Width = 18, CustomWidth = true },
                    new Column { Min = 2, Max = 2, Width = 36, CustomWidth = true },
                    new Column { Min = 3, Max = 3, Width = 72, CustomWidth = true }),
                sheetData);

            AddRow(sheetData, 1, new[] { "Project status update" }, 1);
            AddRow(sheetData, 2, new[] { "Project", document.ProjectName });
            AddRow(sheetData, 3, new[] { "Report date", document.ReportDate });
            AddRow(sheetData, 4, new[] { "Evidence period", $"{document.Period.FromUtc:u} – {document.Period.ToUtc:u}" });
            AddRow(sheetData, 6, new[] { "Project status" }, 1);
            AddRow(sheetData, 7, new[] { document.Output.ProjectStatus }, 2);

            var rowIndex = 9U;
            AddRow(sheetData, rowIndex++, new[] { "Risks" }, 1);
            AddRow(sheetData, rowIndex++, new[] { "New", "Risk", "Description" }, 1);
            foreach (var risk in document.Output.Risks)
            {
                AddRow(sheetData, rowIndex++, new[] { risk.IsNew ? "NEW" : string.Empty, risk.Title, risk.Description });
            }
            if (document.Output.Risks.Count == 0) AddRow(sheetData, rowIndex++, new[] { string.Empty, "No risks reported", string.Empty });

            rowIndex += 1;
            AddRow(sheetData, rowIndex++, new[] { "Open questions" }, 1);
            AddRow(sheetData, rowIndex++, new[] { "New", "Question" }, 1);
            foreach (var question in document.Output.OpenQuestions)
            {
                AddRow(sheetData, rowIndex++, new[] { question.IsNew ? "NEW" : string.Empty, question.Question });
            }
            if (document.Output.OpenQuestions.Count == 0) AddRow(sheetData, rowIndex, new[] { string.Empty, "No open questions reported" });

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Status Summary" });
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    public byte[] CreatePowerPoint(StatusExportDocument document)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            AddTextEntry(archive, "[Content_Types].xml", ContentTypesXml);
            AddTextEntry(archive, "_rels/.rels", RootRelationshipsXml);
            AddTextEntry(archive, "ppt/presentation.xml", PresentationXml);
            AddTextEntry(archive, "ppt/_rels/presentation.xml.rels", PresentationRelationshipsXml);
            AddTextEntry(archive, "ppt/slideMasters/slideMaster1.xml", SlideMasterXml);
            AddTextEntry(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationshipsXml);
            AddTextEntry(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayoutXml);
            AddTextEntry(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationshipsXml);
            AddTextEntry(archive, "ppt/theme/theme1.xml", ThemeXml);
            AddTextEntry(archive, "ppt/slides/slide1.xml", BuildSlideXml(document));
            AddTextEntry(archive, "ppt/slides/_rels/slide1.xml.rels", SlideRelationshipsXml);
            AddTextEntry(archive, "docProps/core.xml", BuildCoreProperties(document));
            AddTextEntry(archive, "docProps/app.xml", AppPropertiesXml);
        }
        return stream.ToArray();
    }

    private static void AddRow(SheetData sheetData, uint index, IReadOnlyList<string> values, uint styleIndex = 0)
    {
        var row = new Row { RowIndex = index };
        for (var column = 0; column < values.Count; column += 1)
        {
            var value = EscapeSpreadsheetFormula(values[column] ?? string.Empty);
            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + column)}{index}",
                DataType = CellValues.InlineString,
                StyleIndex = styleIndex,
                InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve })
            });
        }
        sheetData.Append(row);
    }

    private static string EscapeSpreadsheetFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? $"'{value}" : value;

    private static Stylesheet BuildStyles() => new(
        new Fonts(
            new Font(),
            new Font(new Bold(), new FontSize { Val = 12 })),
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
        new Borders(new Border()),
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }, ApplyAlignment = true }));

    private static void AddTextEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildSlideXml(StatusExportDocument document)
    {
        var risks = document.Output.Risks.Select(risk => $"{(risk.IsNew ? "NEW – " : string.Empty)}{risk.Title}: {risk.Description}").ToList();
        if (risks.Count == 0) risks.Add("No risks reported");
        var questions = document.Output.OpenQuestions.Select(question => $"{(question.IsNew ? "NEW – " : string.Empty)}{question.Question}").ToList();
        if (questions.Count == 0) questions.Add("No open questions reported");

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree>{GroupShapeXml}
            {TextShape(2, "Title", 457200, 180000, 11277600, 550000, $"{document.ProjectName} — Status update ({document.ReportDate})", 2600, true)}
            {TextShape(3, "Status", 457200, 820000, 11277600, 1450000, document.Output.ProjectStatus, 1550, false)}
            {TextShape(4, "Risks", 457200, 2450000, 5400000, 3900000, "RISKS\n" + string.Join("\n", risks.Select(value => "• " + value)), 1350, false)}
            {TextShape(5, "Questions", 6250000, 2450000, 5500000, 3900000, "OPEN QUESTIONS\n" + string.Join("\n", questions.Select(value => "• " + value)), 1350, false)}
            </p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>
            """;
    }

    private static string TextShape(int id, string name, long x, long y, long cx, long cy, string text, int fontSize, bool bold)
    {
        var paragraphs = text.Replace("\r", string.Empty).Split('\n').Select((line, index) =>
            $"<a:p><a:r><a:rPr lang=\"en-US\" sz=\"{fontSize}\" b=\"{(bold || index == 0 ? 1 : 0)}\"/><a:t>{Xml(line)}</a:t></a:r><a:endParaRPr lang=\"en-US\"/></a:p>");
        return $"""
            <p:sp><p:nvSpPr><p:cNvPr id="{id}" name="{Xml(name)}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr><p:txBody><a:bodyPr wrap="square"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</p:txBody></p:sp>
            """;
    }

    private static string Xml(string value) => SecurityElement.Escape(value) ?? string.Empty;
    private static string BuildCoreProperties(StatusExportDocument document) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>{Xml(document.ProjectName)} status update</dc:title><dc:creator>Glance</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:O}</dcterms:created></cp:coreProperties>
        """;

    private const string GroupShapeXml = "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";
    private const string ContentTypesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/><Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/><Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/><Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/><Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/><Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>";
    private const string RootRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>";
    private const string PresentationXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId2\"/></p:sldIdLst><p:sldSz cx=\"12192000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>";
    private const string PresentationRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide1.xml\"/></Relationships>";
    private const string SlideMasterXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldMaster xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:cSld><p:spTree>" + GroupShapeXml + "</p:spTree></p:cSld><p:clrMap accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" bg1=\"lt1\" bg2=\"lt2\" folHlink=\"folHlink\" hlink=\"hlink\" tx1=\"dk1\" tx2=\"dk2\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"1\" r:id=\"rId1\"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>";
    private const string SlideMasterRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme1.xml\"/></Relationships>";
    private const string SlideLayoutXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" type=\"blank\"><p:cSld name=\"Blank\"><p:spTree>" + GroupShapeXml + "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
    private const string SlideLayoutRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>";
    private const string SlideRelationshipsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/></Relationships>";
    private const string ThemeXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Glance\"><a:themeElements><a:clrScheme name=\"Glance\"><a:dk1><a:srgbClr val=\"1F1B16\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"333333\"/></a:dk2><a:lt2><a:srgbClr val=\"F4F0EA\"/></a:lt2><a:accent1><a:srgbClr val=\"806028\"/></a:accent1><a:accent2><a:srgbClr val=\"B95648\"/></a:accent2><a:accent3><a:srgbClr val=\"66805A\"/></a:accent3><a:accent4><a:srgbClr val=\"526C80\"/></a:accent4><a:accent5><a:srgbClr val=\"887090\"/></a:accent5><a:accent6><a:srgbClr val=\"AA7A45\"/></a:accent6><a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme><a:fontScheme name=\"Glance\"><a:majorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme><a:fmtScheme name=\"Glance\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>";
    private const string AppPropertiesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>Glance</Application><Slides>1</Slides><PresentationFormat>Widescreen</PresentationFormat></Properties>";
}
