using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Blazor.Untranslated.Tests;

/// <summary>
/// Packs the analyzer, copies <c>tests/Fixtures/Components</c> to a temporary folder and builds it against the
/// package with <c>dotnet build</c>, once for the whole test class. The fixture builds with the current SDK, or
/// with the newest installed SDK of the version in <c>UNTRANSLATED_FIXTURE_SDK</c> (for example <c>8.0</c>),
/// targeting the matching framework.
/// </summary>
public sealed partial class FixtureBuild : IDisposable
{
    private const string FixtureVersion = "0.0.0-fixture";

    private readonly string work = Directory.CreateTempSubdirectory("untranslated-").FullName;

    public FixtureBuild()
    {
        SourceDirectory = Path.Combine(RepoRoot, "tests", "Fixtures", "Components");
        var feed = Path.Combine(work, "feed");
        var project = Path.Combine(work, "Components");
        var sdk = Environment.GetEnvironmentVariable("UNTRANSLATED_FIXTURE_SDK");

        var pack = Run(RepoRoot, "pack", Path.Combine(RepoRoot, "src", "Blazor.Untranslated", "Blazor.Untranslated.csproj"),
            "-c", "Release", "-o", feed, $"-p:UntranslatedVersion={FixtureVersion}");
        if (pack.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet pack failed:\n{pack.Output}");
        }

        CopyDirectory(SourceDirectory, project);
        // Stop MSBuild and NuGet from picking up anything above the temporary folder, and restore into a private
        // packages folder so the global cache never holds the fixture version.
        File.WriteAllText(Path.Combine(work, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(work, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(work, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="fixture" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        // The console repeats every error in its summary; a file logger with errors only lists each one once.
        var errors = Path.Combine(work, "errors.log");
        var arguments = new List<string>
        {
            "build", project, "-tl:off", "-nr:false",
            $"-flp:LogFile={errors};ErrorsOnly;NoSummary",
            "-p:UseSharedCompilation=false",
            $"-p:UntranslatedVersion={FixtureVersion}",
            $"-p:RestorePackagesPath={Path.Combine(work, "packages")}",
        };
        if (!string.IsNullOrEmpty(sdk))
        {
            File.WriteAllText(Path.Combine(work, "global.json"), $$"""
                { "sdk": { "version": "{{sdk}}.100", "rollForward": "latestFeature" } }
                """);
            arguments.Add($"-p:FixtureTargetFramework=net{sdk}");
        }

        RequestedSdk = sdk;
        SdkVersion = Run(work, "--version").Output.Trim();
        var build = Run(work, [.. arguments]);
        Output = build.Output;
        ExitCode = build.ExitCode;
        Errors = File.Exists(errors) ? File.ReadAllLines(errors).Where(line => line.Trim().Length > 0).ToList() : [];
        Reported = Errors
            .Select(line => ErrorLine().Match(line))
            .Where(match => match.Success)
            .Select(match => new Reported(
                Path.GetFileName(match.Groups["file"].Value),
                int.Parse(match.Groups["line"].Value),
                match.Groups["text"].Value))
            .ToList();
    }

    public string SourceDirectory { get; }

    /// <summary>The value of <c>UNTRANSLATED_FIXTURE_SDK</c>, if any.</summary>
    public string? RequestedSdk { get; }

    /// <summary>The version of the SDK that built the fixture.</summary>
    public string SdkVersion { get; }

    /// <summary>The console output of the fixture build.</summary>
    public string Output { get; }

    public int ExitCode { get; }

    /// <summary>Every error of the fixture build, once each.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Every <c>error UNT001</c> line of the build, by file name and line.</summary>
    public IReadOnlyList<Reported> Reported { get; }

    public static string RepoRoot { get; } = FindRepoRoot();

    public void Dispose()
    {
        try
        {
            Directory.Delete(work, recursive: true);
        }
        catch (IOException)
        {
            // A compiler process can still hold a file for a moment; the folder is in the temp directory anyway.
        }
    }

    private static (int ExitCode, string Output) Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The test host runs inside an SDK that sets MSBuild variables; left in place, they would make the child
        // build use that SDK instead of the one global.json picks.
        foreach (var name in start.Environment.Keys.Where(key => key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(name);
        }

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return (process.ExitCode, output.ToString());
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Blazor.Untranslated.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Blazor.Untranslated.sln not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex("""^\s*(?<file>.+?\.razor)\((?<line>\d+),\d+(?:,\d+,\d+)?\): error UNT001: Literal text "(?<text>.*)" must come from a resource""")]
    private static partial Regex ErrorLine();
}

/// <summary>One reported <c>UNT001</c>: the <c>.razor</c> file name, the 1-based line and the text.</summary>
public sealed record Reported(string File, int Line, string Text);
