using RightClickConvert.Engines;
using RightClickConvert.Infrastructure;
using RightClickConvert.Shell;

namespace RightClickConvert;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConsoleHelper.AttachToParent();

        try
        {
            // A toast click relaunches us with no arguments; nothing to convert.
            if (Notifier.TryHandleActivation())
            {
                return 0;
            }

            return args switch
            {
                ["--install"] => ShellIntegration.Install(),
                ["--uninstall"] => ShellIntegration.Uninstall(),
                ["--list"] => ListFormats(),
                ["--to", ..] => await ConvertAsync(args),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Log.Write($"FATAL {ex}");
            Notifier.Failure("Conversion failed", ex.Message);
            return 1;
        }
    }

    private static async Task<int> ConvertAsync(string[] args)
    {
        // Shape: --to <ext> [--] <path>
        if (args.Length < 3)
        {
            return Usage();
        }

        string targetExt = args[1].TrimStart('.').ToLowerInvariant();
        int pathStart = args[2] == "--" ? 3 : 2;
        if (pathStart >= args.Length)
        {
            return Usage();
        }

        // Rejoin in case a caller passed an unquoted path containing spaces.
        string rawPath = string.Join(' ', args[pathStart..]);

        // Explorer invokes verbs with the working directory set to System32, so the
        // path must be resolved before anything else touches it.
        string input = Path.GetFullPath(rawPath);

        if (!File.Exists(input))
        {
            Notifier.Failure("File not found", input);
            return 1;
        }

        string inputExt = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        Target? target = Formats.Find(inputExt, targetExt);
        if (target is null)
        {
            Notifier.Failure("Unsupported conversion", $".{inputExt} cannot be converted to .{targetExt}");
            return 1;
        }

        string outputDirectory = Path.GetDirectoryName(input)
            ?? throw new InvalidOperationException($"Could not determine a folder for {input}");

        Log.Write($"convert {input} -> .{target.Ext} via {target.Engine}");

        string produced = target.Engine switch
        {
            Engine.ImageSharp => await ImageEngine.ConvertAsync(input, target, outputDirectory),
            _ => await LibreOfficeEngine.ConvertAsync(input, target, outputDirectory),
        };

        Log.Write($"wrote {produced}");
        Console.WriteLine(produced);
        Notifier.Success(produced);
        return 0;
    }

    private static int ListFormats()
    {
        foreach ((string ext, Target[] targets) in Formats.Map.OrderBy(e => e.Key))
        {
            Console.WriteLine($".{ext,-5} -> {string.Join(", ", targets.Select(t => $"{t.Ext} [{t.Engine}]"))}");
        }

        return 0;
    }

    private static int Usage()
    {
        Console.WriteLine("""
            RightClickConvert

              --to <ext> -- <file>   Convert a file and write the result beside it.
              --install              Add the 'Convert' menu to Explorer (current user).
              --uninstall            Remove it.
              --list                 Show every supported conversion.
            """);

        return 2;
    }
}
