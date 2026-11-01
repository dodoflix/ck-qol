using System.Diagnostics;
using System.Text.RegularExpressions;

// Dumps the game into dump/, so reading its internals is a grep instead of another ILSpy run.
// Gitignored: each machine dumps from its own install. Run from the repo root.
//
//   dotnet run --project tools/dump                  everything, skipping what is already current
//   dotnet run --project tools/dump -- code [name]   decompiled assemblies only, optionally filtered
//   dotnet run --project tools/dump -- assets        GameObject hierarchies and sprites only
var repo = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(repo, ".config", "dotnet-tools.json")))
    return Fail("run this from the repo root");

var game = Environment.GetEnvironmentVariable("CK_GAME_DIR");
if (string.IsNullOrEmpty(game))
    game = Path.Combine(
        OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "Steam", "steamapps", "common", "Core Keeper");
var data = Path.Combine(game, "CoreKeeper_Data");
var managed = Path.Combine(data, "Managed");
var output = Path.Combine(repo, "dump");
if (!Directory.Exists(managed))
    return Fail($"game assemblies not found at: {managed}. Set CK_GAME_DIR to override.");

var stage = args.Length > 0 ? args[0] : "all";
var filter = args.Length > 1 ? args[1] : "";
Directory.CreateDirectory(output);

switch (stage)
{
    case "all": if (DumpCode() != 0 || DumpAssets() != 0) return 1; break;
    case "code": if (DumpCode() != 0) return 1; break;
    case "assets": if (DumpAssets() != 0) return 1; break;
    default: return Fail("usage: dump [all|code [name]|assets]");
}
Console.WriteLine();
Console.WriteLine($"{Directory.GetFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1048576} MB in dump/");
return 0;

int DumpCode()
{
    // The BCL is documented upstream and would add sixty assemblies of noise to every grep.
    var bcl = new Regex(@"^(mscorlib|netstandard|System|Microsoft|Mono)\.?[^/\\]*\.dll$", RegexOptions.IgnoreCase);
    var dlls = Directory.GetFiles(managed, "*.dll").OrderBy(f => f, StringComparer.Ordinal)
        .Where(f => !bcl.IsMatch(Path.GetFileName(f)))
        .Where(f => filter == "" || Path.GetFileName(f).Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (dlls.Count == 0) return Fail($"no assemblies matched: {filter}");

    string Decompiled(string dll) => Path.Combine(output, Path.GetFileNameWithoutExtension(dll) + ".decompiled.cs");

    // Only what the last dump missed or a game update replaced.
    var todo = dlls.Where(d => !(File.Exists(Decompiled(d)) && new FileInfo(Decompiled(d)).Length > 0
                                 && File.GetLastWriteTimeUtc(Decompiled(d)) > File.GetLastWriteTimeUtc(d))).ToList();
    if (todo.Count == 0)
    {
        Console.WriteLine($"code: up to date, {dlls.Count} assemblies");
        return 0;
    }

    if (Run("dotnet", ["tool", "restore"], repo, quiet: true) != 0) return Fail("dotnet tool restore failed");
    Console.WriteLine($"code: decompiling {todo.Count} of {dlls.Count} assemblies...");
    Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dll =>
    {
        if (Run("dotnet", ["ilspycmd", "-o", output, "-r", managed, dll], repo, quiet: true) != 0)
        {
            // A partial file would look current to the next run, so drop it on failure.
            File.Delete(Decompiled(dll));
            Console.Error.WriteLine($"failed: {Path.GetFileName(dll)}");
        }
    });
    Console.WriteLine($"  {Directory.GetFiles(output, "*.decompiled.cs").Length} assemblies");
    return 0;
}

int DumpAssets()
{
    var marker = Path.Combine(output, "assets", "resources.assets.tree.txt");
    if (File.Exists(marker) && new FileInfo(marker).Length > 0
        && File.GetLastWriteTimeUtc(marker) > File.GetLastWriteTimeUtc(Path.Combine(data, "resources.assets")))
    {
        Console.WriteLine($"assets: up to date, {Directory.GetFiles(Path.Combine(output, "assets")).Length} trees");
        return 0;
    }

    Console.WriteLine("assets: reading the game's data files...");
    var script = Path.Combine(repo, "scripts", "dump-assets.py");
    if (Run("uv", ["--version"], repo, quiet: true) == 0)
        return Run("uv", ["run", "--quiet", "--with", "UnityPy", "python", script, data, output], repo);

    var venv = Path.Combine(output, ".venv");
    var python = Path.Combine(venv, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");
    if (!File.Exists(python))
    {
        var made = new[] { "python3", "python", "py" }.Any(p => Run(p, ["-m", "venv", venv], repo, quiet: true) == 0);
        if (!made) return Fail("no Python found: install Python 3 or uv");
        if (Run(python, ["-m", "pip", "install", "-q", "UnityPy"], repo) != 0) return Fail("pip install UnityPy failed");
    }
    return Run(python, [script, data, output], repo);
}

static int Run(string file, string[] arguments, string workDir, bool quiet = false)
{
    var info = new ProcessStartInfo(file) { WorkingDirectory = workDir, RedirectStandardOutput = quiet, RedirectStandardError = quiet };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    try
    {
        using var p = Process.Start(info);
        if (quiet) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); }
        p.WaitForExit();
        return p.ExitCode;
    }
    catch (System.ComponentModel.Win32Exception) { return -1; }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
