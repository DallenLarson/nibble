using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

// Checks what Nibble actually ships: the host artifact (optimisations, symbols,
// ReadyToRun images) and the Chromium engine it rides on (path, version, signature).
// Usage: dotnet run --project tools/VerifyBuild -- <published-exe> [--object-dir <dir>]

    var exe = args.Length > 0 ? args[0] : "dist/Nibble.exe";
var objectDir = args.Length > 2 && args[1] == "--object-dir" ? args[2] : null;
var problems = new List<string>();

Console.WriteLine("NIBBLE BUILD VERIFICATION");
Console.WriteLine("=========================");

// ---- host artifact ------------------------------------------------------------------
if (!File.Exists(exe))
{
    Console.WriteLine($"host: MISSING ({exe})");
    return 2;
}

var info = new FileInfo(exe);
Console.WriteLine($"host exe            : {info.FullName}");
Console.WriteLine($"host size           : {info.Length:N0} bytes ({info.Length / 1024.0:N0} KB)");
Console.WriteLine($"host file version   : {FileVersionInfo.GetVersionInfo(exe).FileVersion}");

var sidecarDll = Path.Combine(info.DirectoryName!, "Nibble.dll");
var nativeLoader = Path.Combine(info.DirectoryName!, "WebView2Loader.dll");
Console.WriteLine($"shape               : {(File.Exists(sidecarDll) ? "multi-file (exe + dll + loader)" : "single-file bundle")}");
Console.WriteLine($"native loader       : {(File.Exists(nativeLoader) ? "shipped next to the exe" : "bundled / extracted at first run")}");

// ---- ReadyToRun (the .NET analogue of ahead-of-time compiling our own IL) -----------
// A single-file bundle is an apphost, not a managed assembly, so inspect the real
// assembly when one is available (multi-file publish or an explicit --object-dir).
var managedToInspect = File.Exists(sidecarDll)
    ? sidecarDll
    : objectDir is not null && File.Exists(Path.Combine(objectDir, "Nibble.dll"))
        ? Path.Combine(objectDir, "Nibble.dll")
        : exe;
if (!File.Exists(sidecarDll) && objectDir is not null)
    Console.WriteLine($"inspecting assembly : {managedToInspect}");
var r2r = InspectReadyToRun(managedToInspect);
Console.WriteLine($"ReadyToRun images   : {(r2r.HasValue ? (r2r.Value.ReadyToRun ? "YES" : "no") : "unreadable")}{(r2r is { ReadyToRun: true } ? $" ({r2r.Value.Machine}, managed native header present)" : "")}");
if (r2r is null) problems.Add("could not determine ReadyToRun state (inspect the multi-file publish or pass --object-dir)");
if (r2r is { ReadyToRun: false })
    Console.WriteLine("                      (deliberate: measured neutral at this size - see README build verification)");

// ---- debug symbols / debug build markers -------------------------------------------
var pdb = Path.ChangeExtension(managedToInspect, ".pdb");
Console.WriteLine($"debug symbols       : {(File.Exists(pdb) ? "pdb present" : "none shipped")}");
if (File.Exists(pdb)) problems.Add("a .pdb was shipped with the release build");

var runtimeConfig = Path.ChangeExtension(managedToInspect, ".runtimeconfig.json");
if (File.Exists(runtimeConfig))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(runtimeConfig));
    if (doc.RootElement.TryGetProperty("runtimeOptions", out var options) &&
        options.TryGetProperty("configProperties", out var props))
    {
        var interesting = props.EnumerateObject()
            .Where(p => p.Name.Contains("Tiered", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("GC.", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Threadpool", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Name}={p.Value}");
        foreach (var line in interesting) Console.WriteLine($"runtime setting     : {line}");
    }
}
else
{
    Console.WriteLine("runtime settings    : bundled inside the single-file host");
}

// ---- the Chromium engine we actually run -------------------------------------------
var engineRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    "Microsoft", "EdgeWebView", "Application");

if (!Directory.Exists(engineRoot))
{
    problems.Add("WebView2 runtime not found - the browser engine is missing");
    Console.WriteLine("engine              : NOT INSTALLED");
}
else
{
    var versions = Directory.GetDirectories(engineRoot)
        .Select(Path.GetFileName)
        .Where(name => Version.TryParse(name, out _))
        .Select(name => Version.Parse(name!))
        .OrderByDescending(v => v)
        .ToList();

    Console.WriteLine($"engine root         : {engineRoot}");
    Console.WriteLine($"engine versions     : {string.Join(", ", versions)} ({(versions.Count == 1 ? "one" : "newest is used by default")})");

    var newest = versions.First();
    var engineDir = Path.Combine(engineRoot, newest.ToString());
    var engineExe = Path.Combine(engineDir, "msedgewebview2.exe");
    var files = Directory.GetFiles(engineDir, "*", SearchOption.AllDirectories);
    var bytes = files.Sum(f => new FileInfo(f).Length);

    Console.WriteLine($"engine binary       : {engineExe}");
    Console.WriteLine($"engine version      : {FileVersionInfo.GetVersionInfo(engineExe).FileVersion}");
    Console.WriteLine($"engine payload      : {bytes / 1024.0 / 1024.0:N0} MB across {files.Length} files (vendor-built, shared)");

    try
    {
        var cert = X509Certificate.CreateFromSignedFile(engineExe);
        Console.WriteLine($"engine signature    : {cert.Subject}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"engine signature    : UNVERIFIED ({ex.GetType().Name})");
        problems.Add("engine binary signature could not be checked");
    }

    var snapshot = Path.Combine(engineDir, "v8_context_snapshot.bin");
    Console.WriteLine($"v8 external snapshot: {(File.Exists(snapshot) ? "present (release-style build layout)" : "absent")}");
    Console.WriteLine("engine optimisation : vendor-supplied Microsoft release build (signed, updated by Edge Update)");
    Console.WriteLine("                      PGO/ThinLTO are build-time decisions and are NOT inspectable in a shipped binary;");
    Console.WriteLine("                      they are verified by the build configuration (is_official_build, chrome_pgo_phase, use_thin_lto).");
}

Console.WriteLine();
if (problems.Count == 0)
{
    Console.WriteLine("RESULT: all checks passed.");
    return 0;
}

Console.WriteLine("RESULT: issues found");
foreach (var problem in problems) Console.WriteLine($"  - {problem}");
return 1;

static (bool ReadyToRun, string Machine)? InspectReadyToRun(string path)
{
    try
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return null;

        var headers = pe.PEHeaders;
        var corHeader = headers.CorHeader;
        if (corHeader is null) return null;

        // Managed native header = the ReadyToRun (crossgen'd) image payload.
        var hasNative = corHeader.ManagedNativeHeaderDirectory.Size > 0;
        return (hasNative, headers.CoffHeader.Machine.ToString());
    }
    catch
    {
        return null;
    }
}
