using System.Text.RegularExpressions;

namespace Blazor.Untranslated.Tests;

/// <summary>
/// Compares what the analyzer reports in each fixture component with the <c>@*UNT001*@</c> markers in it: one
/// marker per expected diagnostic, on the line the diagnostic points at. A file without markers must build clean.
/// </summary>
public sealed partial class FixtureTests(FixtureBuild build) : IClassFixture<FixtureBuild>
{
    public static TheoryData<string> Components()
    {
        var data = new TheoryData<string>();
        var directory = Path.Combine(FixtureBuild.RepoRoot, "tests", "Fixtures", "Components");
        foreach (var file in Directory.EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Components))]
    public void Reports_exactly_the_marked_lines(string component)
    {
        var expected = File.ReadAllLines(Path.Combine(build.SourceDirectory, component))
            .SelectMany((line, index) => Enumerable.Repeat(index + 1, Marker().Matches(line).Count))
            .Order()
            .ToList();
        var reported = build.Reported
            .Where(diagnostic => diagnostic.File == Path.GetFileName(component))
            .ToList();

        Assert.True(
            expected.SequenceEqual(reported.Select(diagnostic => diagnostic.Line).Order()),
            $"""
            {component}
            expected UNT001 on lines: {string.Join(", ", expected)}
            reported: {string.Join(", ", reported.Select(diagnostic => $"{diagnostic.Line} \"{diagnostic.Text}\""))}
            """);
    }

    [Fact]
    public void Fails_the_build_with_nothing_but_UNT001()
    {
        Assert.NotEqual(0, build.ExitCode);
        Assert.True(build.Reported.Count > 0 && build.Reported.Count == build.Errors.Count, build.Output);
    }

    [GeneratedRegex(@"@\*\s*UNT001\s*\*@")]
    private static partial Regex Marker();
}
