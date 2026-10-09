using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Blazor.Untranslated;

/// <summary>
/// Fails the build when a component renders literal text instead of a resource. It reads the C# that the
/// Razor compiler generates from each <c>.razor</c> file and inspects what reaches <c>RenderTreeBuilder</c>:
/// markup, text content, visible attributes and text parameters of components. Text that letters remain in,
/// after removing the allowed terms in the <c>UntranslatedAllowedTerms</c> MSBuild property (separated by
/// commas), is reported at the <c>.razor</c> file. <c>UntranslatedTextAttributes</c> adds attribute and
/// parameter names to the default set.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LiteralTextAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "UNT001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Text must come from a resource",
        messageFormat: "Literal text \"{0}\" must come from a resource",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "User-visible text in Blazor components comes from localized resources, so that every page can be translated. Terms that are the same in every language go in the UntranslatedAllowedTerms MSBuild property.",
        helpLinkUri: "https://github.com/vmihalachi/blazor-untranslated#readme");

    // HTML attributes people see or hear, and component parameters that carry visible text.
    private static readonly ImmutableHashSet<string> DefaultTextAttributes = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "title",
        "alt",
        "placeholder",
        "label",
        "aria-label",
        "aria-description",
        "aria-roledescription",
        "aria-valuetext",
        "aria-placeholder",
        "description",
        "text");

    private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ScriptOrStyle = new(@"<(script|style)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(@"<(?:""[^""]*""|'[^']*'|[^'"">])*>", RegexOptions.CultureInvariant);
    private static readonly Regex Attribute = new(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        // Razor output is generated code, which analyzers skip unless asked.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var builder = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder");
            if (builder is null)
            {
                return;
            }

            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            var allowed = List(options, "UntranslatedAllowedTerms")
                .OrderByDescending(term => term.Length)
                .ToImmutableArray();
            var textAttributes = DefaultTextAttributes.Union(List(options, "UntranslatedTextAttributes"));
            var razorFiles = start.Options.AdditionalFiles
                .Where(file => file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray();

            start.RegisterSemanticModelAction(model => Analyze(model, builder, allowed, textAttributes, razorFiles));
        });
    }

    // Commas separate the items: the analyzer config that carries MSBuild properties treats ';' and '#' as the
    // start of a comment.
    private static IEnumerable<string> List(AnalyzerConfigOptions options, string property)
    {
        options.TryGetValue("build_property." + property, out var value);
        return (value ?? "")
            .Split(',')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0);
    }

    private static void Analyze(
        SemanticModelAnalysisContext context,
        INamedTypeSymbol builder,
        ImmutableArray<string> allowed,
        ImmutableHashSet<string> textAttributes,
        ImmutableArray<AdditionalText> razorFiles)
    {
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);
        var razorPath = RazorSourcePath(root);
        if (razorPath is null)
        {
            return;
        }

        var source = razorFiles.FirstOrDefault(file => SamePath(file.Path, razorPath))?.GetText(context.CancellationToken);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
                || !SymbolEqualityComparer.Default.Equals(method.ContainingType, builder))
            {
                continue;
            }

            foreach (var text in VisibleTexts(context.SemanticModel, root, textAttributes, method.Name, invocation.ArgumentList.Arguments))
            {
                var shown = Whitespace.Replace(text, " ").Trim();
                if (HasLetters(shown, allowed))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Locate(razorPath, source, shown), Shorten(shown)));
                }
            }
        }
    }

    // Razor writes the source file's path into a #pragma checksum at the top of every generated file.
    private static string? RazorSourcePath(SyntaxNode root)
    {
        foreach (var trivia in root.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is PragmaChecksumDirectiveTriviaSyntax checksum
                && checksum.File.ValueText.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                return checksum.File.ValueText;
            }
        }

        return null;
    }

    private static IEnumerable<string> VisibleTexts(
        SemanticModel model,
        SyntaxNode root,
        ImmutableHashSet<string> textAttributes,
        string method,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        switch (method)
        {
            case "AddMarkupContent" when arguments.Count == 2:
                return Literals(arguments[1].Expression).SelectMany(markup => MarkupTexts(markup, textAttributes));
            case "AddContent" when arguments.Count == 2:
                return Literals(arguments[1].Expression);
            case "AddAttribute" or "AddComponentParameter" when arguments.Count == 3 && IsTextName(model, textAttributes, arguments[1].Expression):
                return Literals(arguments[2].Expression).Concat(CallSiteLiterals(model, root, arguments[2].Expression));
            default:
                return Enumerable.Empty<string>();
        }
    }

    // Element attributes are named by string literals, component parameters by nameof(Component.Parameter);
    // both are constants.
    private static bool IsTextName(SemanticModel model, ImmutableHashSet<string> textAttributes, ExpressionSyntax name) =>
        model.GetConstantValue(name) is { HasValue: true, Value: string value } && textAttributes.Contains(value);

    // Razor renders a generic component through a generated TypeInference helper, so the literal is an
    // argument at the call site and the helper passes it on as a parameter. Follow the parameter back to
    // the calls in the same file.
    private static IEnumerable<string> CallSiteLiterals(SemanticModel model, SyntaxNode root, ExpressionSyntax value)
    {
        if (Unwrap(value) is not IdentifierNameSyntax name
            || model.GetSymbolInfo(name).Symbol is not IParameterSymbol { ContainingSymbol: IMethodSymbol helper } parameter
            || helper.ContainingType?.Name != "TypeInference")
        {
            return Enumerable.Empty<string>();
        }

        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.ArgumentList.Arguments.Count > parameter.Ordinal
                && model.GetSymbolInfo(call).Symbol is IMethodSymbol target
                && SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, helper.OriginalDefinition))
            .SelectMany(call => Literals(call.ArgumentList.Arguments[parameter.Ordinal].Expression));
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } check when IsTypeCheck(check.Expression)
            => Unwrap(check.ArgumentList.Arguments[0].Expression),
        _ => expression,
    };

    // The string literals an expression can evaluate to, looking through casts, conditionals,
    // concatenation, interpolation and the compiler's type checks.
    private static IEnumerable<string> Literals(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                yield return literal.Token.ValueText;
                break;
            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var part in interpolated.Contents.OfType<InterpolatedStringTextSyntax>())
                {
                    yield return part.TextToken.ValueText;
                }

                break;
            case ParenthesizedExpressionSyntax parenthesized:
                foreach (var text in Literals(parenthesized.Expression))
                {
                    yield return text;
                }

                break;
            case CastExpressionSyntax cast:
                foreach (var text in Literals(cast.Expression))
                {
                    yield return text;
                }

                break;
            case ConditionalExpressionSyntax conditional:
                foreach (var text in Literals(conditional.WhenTrue).Concat(Literals(conditional.WhenFalse)))
                {
                    yield return text;
                }

                break;
            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                foreach (var text in Literals(coalesce.Right))
                {
                    yield return text;
                }

                break;
            case BinaryExpressionSyntax concatenation when concatenation.IsKind(SyntaxKind.AddExpression):
                foreach (var text in Literals(concatenation.Left).Concat(Literals(concatenation.Right)))
                {
                    yield return text;
                }

                break;
            case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } check when IsTypeCheck(check.Expression):
                foreach (var text in Literals(check.ArgumentList.Arguments[0].Expression))
                {
                    yield return text;
                }

                break;
        }
    }

    private static bool IsTypeCheck(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "TypeCheck" };

    // Static markup arrives as one HTML string: check its text nodes and visible attributes.
    private static IEnumerable<string> MarkupTexts(string markup, ImmutableHashSet<string> textAttributes)
    {
        var html = ScriptOrStyle.Replace(Comment.Replace(markup, " "), " ");

        foreach (Match tag in Tag.Matches(html))
        {
            foreach (Match attribute in Attribute.Matches(tag.Value))
            {
                if (textAttributes.Contains(attribute.Groups[1].Value))
                {
                    var value = attribute.Groups[2].Success ? attribute.Groups[2].Value : attribute.Groups[3].Value;
                    yield return WebUtility.HtmlDecode(value);
                }
            }
        }

        foreach (var text in Tag.Split(html))
        {
            yield return WebUtility.HtmlDecode(text);
        }
    }

    private static bool HasLetters(string text, ImmutableArray<string> allowed)
    {
        foreach (var term in allowed)
        {
            text = text.Replace(term, " ");
        }

        return text.Any(char.IsLetter);
    }

    // Points at the first occurrence of the text in the .razor file, or at its start when the text is
    // written differently there (an entity, a line break).
    private static Location Locate(string path, SourceText? source, string text)
    {
        var start = source?.ToString().IndexOf(text, StringComparison.Ordinal) ?? -1;
        if (source is null || start < 0)
        {
            return Location.Create(path, default, default);
        }

        var span = new TextSpan(start, text.Length);
        return Location.Create(path, span, source.Lines.GetLinePositionSpan(span));
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.Ordinal);

    private static string Shorten(string text) => text.Length <= 60 ? text : text.Substring(0, 57) + "...";
}
