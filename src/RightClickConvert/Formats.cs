namespace RightClickConvert;

public enum Engine
{
    LibreOffice,
    ImageSharp,
}

/// <param name="Ext">Output extension without a dot. Unique within one input's target list — this is what <c>--to</c> matches on.</param>
/// <param name="Label">Text shown in the Explorer submenu.</param>
/// <param name="Filter">The <c>--convert-to</c> argument for LibreOffice. Null for ImageSharp targets.</param>
public sealed record Target(string Ext, string Label, Engine Engine, string? Filter = null);

/// <summary>
/// The single source of truth for what can be converted into what. Both the runtime
/// dispatch and the Explorer submenus written by <see cref="ShellIntegration"/> read
/// from here, so the menu can never offer something the code can't do.
/// </summary>
public static class Formats
{
    // Filter names verified against share/registry/*.xcd of LibreOffice 26.2.
    // The module prefix on the PDF filters is load-bearing: pass writer_pdf_Export
    // for a spreadsheet and LibreOffice exits 0 having produced nothing.
    private static readonly Target PdfWriter = new("pdf", "PDF", Engine.LibreOffice, "pdf:writer_pdf_Export");
    private static readonly Target PdfCalc = new("pdf", "PDF", Engine.LibreOffice, "pdf:calc_pdf_Export");
    private static readonly Target PdfImpress = new("pdf", "PDF", Engine.LibreOffice, "pdf:impress_pdf_Export");
    private static readonly Target PdfDraw = new("pdf", "PDF", Engine.LibreOffice, "pdf:draw_pdf_Export");

    private static readonly Target Docx = new("docx", "Word Document (.docx)", Engine.LibreOffice, "docx:MS Word 2007 XML");
    private static readonly Target Odt = new("odt", "OpenDocument Text (.odt)", Engine.LibreOffice, "odt:writer8");
    private static readonly Target Md = new("md", "Markdown (.md)", Engine.LibreOffice, "md:Markdown");
    private static readonly Target Txt = new("txt", "Plain Text (.txt)", Engine.LibreOffice, "txt:Text");

    private static readonly Target Xlsx = new("xlsx", "Excel Workbook (.xlsx)", Engine.LibreOffice, "xlsx:Calc MS Excel 2007 XML");
    private static readonly Target Ods = new("ods", "OpenDocument Spreadsheet (.ods)", Engine.LibreOffice, "ods:calc8");
    private static readonly Target Csv = new("csv", "CSV (.csv)", Engine.LibreOffice, "csv:Text - txt - csv (StarCalc)");

    private static readonly Target Pptx = new("pptx", "PowerPoint (.pptx)", Engine.LibreOffice, "pptx:Impress MS PowerPoint 2007 XML");
    private static readonly Target Odp = new("odp", "OpenDocument Presentation (.odp)", Engine.LibreOffice, "odp:impress8");

    // Rasterising a vector or laying a bitmap onto a PDF page is Draw's job; ImageSharp
    // can do neither.
    private static readonly Target PngDraw = new("png", "PNG (.png)", Engine.LibreOffice, "png:draw_png_Export");
    private static readonly Target JpgDraw = new("jpg", "JPEG (.jpg)", Engine.LibreOffice, "jpg:draw_jpg_Export");

    private static readonly Target PngImage = new("png", "PNG (.png)", Engine.ImageSharp);
    private static readonly Target JpgImage = new("jpg", "JPEG (.jpg)", Engine.ImageSharp);
    private static readonly Target WebpImage = new("webp", "WebP (.webp)", Engine.ImageSharp);

    private static readonly Target[] WriterTargets = [PdfWriter, Docx, Odt, Md, Txt];
    private static readonly Target[] CalcTargets = [PdfCalc, Xlsx, Ods, Csv];
    private static readonly Target[] ImpressTargets = [PdfImpress, Pptx, Odp];
    private static readonly Target[] VectorTargets = [PdfDraw, PngDraw, JpgDraw];
    private static readonly Target[] RasterTargets = [PngImage, JpgImage, WebpImage, PdfDraw];

    /// <summary>Input extension (no dot) to the conversions offered for it, in menu order.</summary>
    public static readonly IReadOnlyDictionary<string, Target[]> Map = Build();

    public static Target? Find(string inputExt, string targetExt) =>
        Map.TryGetValue(inputExt, out var targets)
            ? targets.FirstOrDefault(t => t.Ext.Equals(targetExt, StringComparison.OrdinalIgnoreCase))
            : null;

    private static Dictionary<string, Target[]> Build()
    {
        var map = new Dictionary<string, Target[]>(StringComparer.OrdinalIgnoreCase);

        void Add(Target[] targets, params string[] inputs)
        {
            foreach (var input in inputs)
            {
                // Never offer a file its own format.
                map[input] = targets.Where(t => !SameFormat(t.Ext, input)).ToArray();
            }
        }

        Add(WriterTargets, "docx", "doc", "odt", "rtf", "txt", "md", "html", "htm");
        Add(CalcTargets, "xlsx", "xls", "ods", "csv");
        Add(ImpressTargets, "pptx", "ppt", "odp");
        Add(VectorTargets, "svg");
        Add(RasterTargets, "png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff", "gif");

        return map;
    }

    private static bool SameFormat(string a, string b) =>
        Canonical(a) == Canonical(b);

    private static string Canonical(string ext) => ext.ToLowerInvariant() switch
    {
        "jpeg" => "jpg",
        "tiff" => "tif",
        "htm" => "html",
        var other => other,
    };
}
