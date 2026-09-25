# RightClickConvert

A Windows file converter that lives in the Explorer context menu. Right-click a file,
pick **Convert**, choose a target format, and the converted file appears next to the
original.

Documents go through a headless LibreOffice process; images are converted in-process
with ImageSharp. Nothing is uploaded anywhere.

---

## Supported conversions

| Input | Converts to |
|---|---|
| `.docx` `.doc` `.odt` `.rtf` `.txt` `.md` `.html` `.htm` | PDF, DOCX, ODT, Markdown, TXT |
| `.xlsx` `.xls` `.ods` `.csv` | PDF, XLSX, ODS, CSV |
| `.pptx` `.ppt` `.odp` | PDF, PPTX, ODP |
| `.png` `.jpg` `.jpeg` `.webp` `.bmp` `.tif` `.tiff` `.gif` | PNG, JPEG, WebP, PDF |
| `.svg` | PDF, PNG, JPEG |

A file is never offered its own format. Run `RightClickConvert.exe --list` to print the
exact matrix.

Conversions are always **within a document type** — that is a LibreOffice constraint, not
a missing feature. There is no `.docx` → `.pptx` or `.xlsx` → `.docx`, because no such
import/export path exists.

---

## Requirements

| | |
|---|---|
| Windows | 10 build 19041 or later |
| [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) | required |
| [LibreOffice](https://www.libreoffice.org/download/) | required for **document** conversions only |

Image conversions (`png`/`jpg`/`webp`) work without LibreOffice installed. Everything else
needs it. The app finds LibreOffice via the registry (`HKLM\SOFTWARE\LibreOffice\UNO\InstallPath`)
and falls back to the standard Program Files locations.

---

## Install

Publish to a stable location — the registry stores an absolute path to the executable, so
installing from `bin\Debug` breaks the moment you clean or move the repo.

```powershell
dotnet publish src\RightClickConvert\RightClickConvert.csproj -c Release -o "C:\Tools\RightClickConvert"
& "C:\Tools\RightClickConvert\RightClickConvert.exe" --install
```

On **Windows 11** the entry appears under **Show more options** (`Shift+F10`), not in the
compact context menu. Reaching the top-level menu requires an `IExplorerCommand` COM
handler in a signed MSIX package.

If the menu does not appear, restart Explorer:

```powershell
Stop-Process -Name explorer -Force
```

### Uninstall

```powershell
& "C:\Tools\RightClickConvert\RightClickConvert.exe" --uninstall
```

Always run this **before** deleting the files. Removing the executable first orphans the
registry entries, leaving menu items that silently do nothing.

---

## Command line

```
--to <ext> -- <file>   Convert a file, writing the result beside it
--install              Add the Convert menu for the current user
--uninstall            Remove it
--list                 Print every supported conversion
```

The `--` separator matters: without it a filename beginning with `-` is parsed as a flag.

---

## How it works

### Two engines

Routing is by **conversion pair**, not by file type:

```
raster input AND target is png/jpg/webp  ->  ImageSharp
everything else                          ->  LibreOffice
```

That split is why `jpg → png` stays in-process while `svg → png` and `png → pdf` go to
LibreOffice — ImageSharp can neither rasterise vectors nor write PDFs.

### One source of truth

[`Formats.cs`](src/RightClickConvert/Formats.cs) maps every input extension to its targets,
each carrying a label, an engine, and a LibreOffice filter string. Both runtime dispatch
and the Explorer submenus are generated from it, so the menu cannot offer a conversion the
code does not implement. Filter names were verified against the filter registry
(`share/registry/*.xcd`) of LibreOffice 26.2.

The module prefix on the PDF filters is load-bearing — passing `writer_pdf_Export` for a
spreadsheet makes LibreOffice exit 0 having produced nothing.

### Details that matter

- **Private LibreOffice profile.** Each conversion runs with
  `-env:UserInstallation=...`. Sharing the default profile makes headless conversion
  silently do nothing whenever the user has LibreOffice open — the common case for a
  right-click tool.
- **Serialised.** Explorer launches the verb once per selected file, so a 15-file
  selection would start 15 `soffice` processes. A named mutex serialises them.
- **No overwrites.** Output goes to a scratch directory first, then moves to a
  non-colliding name (`report (1).pdf`).
- **Timeout.** `soffice` is killed after 90 seconds.

---

## Project layout

```
src/RightClickConvert/
  Program.cs               Entry point, argument parsing, routing
  Formats.cs               The conversion table (single source of truth)
  Engines/
    LibreOfficeEngine.cs   Process launch, discovery, serialisation, timeout
    ImageEngine.cs         Raster conversions via ImageSharp
  Shell/
    ShellIntegration.cs    Registry entries for the context menu
    Notifier.cs            Toast notifications
  Infrastructure/
    Paths.cs               Collision-free output naming
    Log.cs                 Append-only log
    ConsoleHelper.cs       Parent-console attach for terminal use
```

---

## Development

```powershell
dotnet build
dotnet run --project src\RightClickConvert -- --list
```

The project is a **WinExe** so Explorer never flashes a console window. That also means
console output is invisible from a terminal, which `ConsoleHelper.AttachToParent()` works
around by attaching to the parent console at startup.

Testing a conversion without touching the registry:

```powershell
& "C:\Tools\RightClickConvert\RightClickConvert.exe" --to pdf -- "C:\path\to\file.docx"
```

---

## Troubleshooting

**Menu doesn't appear.** Check the entries exist, then restart Explorer:

```powershell
Get-ChildItem HKCU:\Software\Classes\SystemFileAssociations |
  Where-Object { Test-Path "$($_.PSPath)\shell\RightClickConvert" } |
  Measure-Object
```

**A conversion fails.** `%LOCALAPPDATA%\RightClickConvert\log.txt` records the full
`soffice` command line, its exit code, and stderr.

**Menu items do nothing.** The executable moved. Re-run `--install` from its new location.

---

## Known limitations

- **`image → PDF` produces an A4 page**, not a page sized to the image, so a small image
  ends up centred on a large sheet. Fixing this means routing raster→PDF through a PDF
  library instead of LibreOffice Draw.
- **`xlsx → csv` exports only the first sheet.** Standard LibreOffice behaviour.
- **`→ md` and `→ txt` discard images and most formatting**, as the formats require.
- **Animated GIF/WebP inputs** convert to a still image from the first frame.
- **HEIC/HEIF is not supported.** LibreOffice registers no HEIC import filter and
  ImageSharp cannot decode it; support would need `libheif` or the Windows HEIF codec.

---

## Licensing notes

- **ImageSharp is pinned to 3.1.x** deliberately. Version 4.x requires a paid Six Labors
  license key and warns on every build. 3.1.x is the Six Labors Split License — free for
  personal and open-source use; commercial use above their revenue threshold requires a
  license.
- **Notifications use the classic Win32 toast API** via
  `Microsoft.Toolkit.Uwp.Notifications`. The Windows App SDK was removed: it was
  referenced solely for toasts and accounted for roughly 140 MB of publish output —
  including ONNX Runtime and DirectML — that this app never used. Dropping it took the
  framework-dependent publish from **157.6 MB / 63 files to 28.0 MB / 13 files**. The
  package is in maintenance mode; the zero-dependency alternative is
  `Windows.UI.Notifications` directly, which costs roughly 150 lines of `IShellLink`
  interop to register the AUMID by hand.
- **`System.Drawing.Common` is pinned to 9.0.0** to override the 4.7.0 that the
  notifications package pulls transitively, which carries a critical advisory
  (GHSA-rxg9-xrhp-64gj).
- **LibreOffice is not bundled** and must be installed separately.

Of the remaining 28 MB, 23.7 MB is `Microsoft.Windows.SDK.NET.dll` — the WinRT
projections that come with the `net10.0-windows...` target framework. Trimming reduces it
substantially if size matters further.
