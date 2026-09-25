# RightClickConvert

A Windows file converter present in the Right Click Menu.
Right-click a file, pick **Convert**, choose a target format, and the converted file appears next to the
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


---

## Requirements

| | |
|---|---|
| Windows | 10 build 19041 or later |
| [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) | required |
| [LibreOffice](https://www.libreoffice.org/download/) | required for **document** conversions|

Image conversions (`png`/`jpg`/`webp`) work without LibreOffice installed, Documents need it however. The app finds LibreOffice via the registry (`HKLM\SOFTWARE\LibreOffice\UNO\InstallPath`)
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
registry entries, leaving menu items that do nothing.

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

### Two parts

Routing is by **conversion pair**, not by file type:

```
raster input AND target is png/jpg/webp  ->  ImageSharp
everything else                          ->  LibreOffice
```

### Notes

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

## Known limitations

- **`image → PDF` produces an A4 page**, not a page sized to the image, so a small image
  ends up centred on a large sheet. Fixing this means routing raster→PDF through a PDF
  library instead of LibreOffice Draw.
- **`xlsx → csv` exports only the first sheet.** Standard LibreOffice behaviour.
- **`→ md` and `→ txt` discard images and most formatting**, as the formats require.
- **Animated GIF/WebP inputs** convert to a still image from the first frame.
- **HEIC/HEIF is not supported.** LibreOffice registers no HEIC import filter and
  ImageSharp cannot decode it; support would need `libheif` or the Windows HEIF codec.

