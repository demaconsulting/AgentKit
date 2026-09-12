using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DemaConsulting.AgentKit.Tests.Shared;

/// <summary>
///     Compiles every <c>&lt;example&gt;&lt;code&gt;</c> block a shipped package publishes in its XML
///     documentation file, so a documented example cannot describe an API the code does not have.
/// </summary>
/// <remarks>
///     <para>
///     The documented examples are the assembly instructions a coding agent follows to build an
///     application against AgentKit, and an example that does not compile is worse than no example
///     at all: it is a confident, plausible-looking instruction that fails only once the reader has
///     acted on it. Several defects in this project have come from documentation asserting things
///     the code did not do, so the examples are verified against the real compiled API rather than
///     reviewed by eye.
///     </para>
///     <para>
///     The <em>shipped XML documentation file</em> is the subject, not the source files. That is
///     what a consumer's IDE shows, what the packaged API reference is generated from, and what
///     therefore has to be true. Reading it back from the build output also proves the package
///     actually emits documentation at all.
///     </para>
///     <para>
///     A snippet is written for a reader, not for a compiler, so there is no single correct way to
///     wrap it. Three candidate wrappings are tried — a whole file, a class body, and a method body
///     — and the snippet passes if any of them compiles. When none does, the diagnostics of the
///     <em>best</em> candidate (the one with the fewest errors) are reported, because those are the
///     ones that describe the real mistake rather than the consequences of a wrapping that never
///     fitted.
///     </para>
///     <para>
///     This file is linked into each package's test project rather than shared through a project
///     reference, because a test project that referenced a common library would publish that
///     library's assembly into its own output and change what the verifier sees.
///     </para>
/// </remarks>
internal static class XmlDocExampleVerifier
{
    /// <summary>
    ///     The usings every snippet is compiled with, so examples show composition rather than
    ///     boilerplate a reader would have to mentally discard.
    /// </summary>
    private static readonly string[] CommonUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks"
    ];

    /// <summary>
    ///     Compiles every documented example in a package's XML documentation file and fails the
    ///     calling test if any of them does not compile.
    /// </summary>
    /// <remarks>
    ///     Finding no examples is itself a failure. A missing or empty documentation file would
    ///     otherwise let this verification pass while proving nothing, which is exactly the silent
    ///     false confidence it exists to prevent.
    /// </remarks>
    /// <param name="xmlDocumentationFileName">
    ///     The file name (not path) of the package's XML documentation file as it appears in the
    ///     test output folder, for example <c>DemaConsulting.AgentKit.Core.xml</c>.
    /// </param>
    /// <param name="additionalUsings">
    ///     The package namespaces a snippet may use without writing a using directive, for example
    ///     the namespace of the type being documented.
    /// </param>
    public static void VerifyExamples(string xmlDocumentationFileName, params string[] additionalUsings)
    {
        var documentationPath = Path.Combine(AppContext.BaseDirectory, xmlDocumentationFileName);
        Assert.True(
            File.Exists(documentationPath),
            $"The XML documentation file '{xmlDocumentationFileName}' was not found in the test output "
            + $"folder '{AppContext.BaseDirectory}'. The package must build with "
            + "GenerateDocumentationFile enabled for its examples to be verifiable.");

        var examples = ExtractExamples(documentationPath);
        Assert.True(
            examples.Count > 0,
            $"No <example><code> blocks were found in '{xmlDocumentationFileName}'. The package is "
            + "expected to document its consumer-facing API with compilable examples.");

        var references = BuildReferences();
        var prologue = BuildPrologue(additionalUsings);

        var failures = examples
            .Select(example => TryCompile(example, prologue, references))
            .Where(failure => failure is not null)
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {examples.Count} documented example(s) in "
            + $"'{xmlDocumentationFileName}' do not compile against the real API:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     Reads every <c>&lt;example&gt;&lt;code&gt;</c> block out of an XML documentation file.
    /// </summary>
    /// <remarks>
    ///     Only <c>&lt;code&gt;</c> content is taken. Prose inside an <c>&lt;example&gt;</c> explains
    ///     the snippet and is not itself code, so compiling it would report failures that say
    ///     nothing about the API.
    /// </remarks>
    /// <param name="documentationPath">The full path of the XML documentation file.</param>
    /// <returns>The body of each documented example, with its comment indentation removed, in document order.</returns>
    private static List<string> ExtractExamples(string documentationPath)
    {
        var document = XDocument.Load(documentationPath);

        return
        [
            .. document
                .Descendants("example")
                .Elements("code")
                .Select(code => Dedent(code.Value))
                .Where(snippet => !string.IsNullOrWhiteSpace(snippet))
        ];
    }

    /// <summary>
    ///     Removes the common leading indentation an XML documentation comment adds to every line of
    ///     a snippet.
    /// </summary>
    /// <remarks>
    ///     The indentation is an artifact of the comment the snippet was written in, not of the
    ///     snippet. Leaving it in place compiles correctly but makes every reported diagnostic
    ///     column misleading.
    /// </remarks>
    /// <param name="code">The raw text of a <c>&lt;code&gt;</c> element.</param>
    /// <returns>The snippet with its common indentation and surrounding blank lines removed.</returns>
    private static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var indent = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var stripped = lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? string.Empty : line[indent..]);

        return string.Join(Environment.NewLine, stripped).Trim();
    }

    /// <summary>
    ///     Builds the using directives every snippet is compiled with.
    /// </summary>
    /// <param name="additionalUsings">The package namespaces to add to the common set.</param>
    /// <returns>The prologue text, ending with a line break.</returns>
    private static string BuildPrologue(IEnumerable<string> additionalUsings)
    {
        var builder = new StringBuilder();

        foreach (var name in CommonUsings.Concat(additionalUsings).Distinct(StringComparer.Ordinal))
        {
            builder.AppendLine($"using {name};");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Collects the metadata references a snippet is compiled against.
    /// </summary>
    /// <remarks>
    ///     The test process's own trusted-platform assembly list is used, because it already
    ///     contains exactly the framework and package assemblies the subject package was built
    ///     against. References are deduplicated by simple name: the list can legitimately contain
    ///     two files providing one assembly, which the compiler reports as a duplicate-reference
    ///     error that has nothing to do with the snippet.
    /// </remarks>
    /// <returns>The deduplicated metadata references.</returns>
    private static List<MetadataReference> BuildReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();

        // A host that does not publish the trusted-platform list still has every assembly it loaded
        // sitting beside the test binary, which is an equivalent source.
        if (paths is null || paths.Count == 0)
        {
            paths = [.. Directory.GetFiles(AppContext.BaseDirectory, "*.dll")];
        }

        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Where(path => seen.Add(SimpleName(path))))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }

    /// <summary>
    ///     Determines the assembly simple name a file provides, used to deduplicate references.
    /// </summary>
    /// <remarks>
    ///     The file name is used when the file cannot be read as a managed assembly — a native
    ///     library beside the test binary, for instance — so that such a file is still counted once
    ///     and never crashes the collection.
    /// </remarks>
    /// <param name="path">The full path of a candidate reference.</param>
    /// <returns>The assembly simple name, or the file name when it cannot be determined.</returns>
    private static string SimpleName(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Name ?? Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException
                                              or IOException or ArgumentException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    /// <summary>
    ///     Compiles one snippet, trying each candidate wrapping in turn.
    /// </summary>
    /// <param name="snippet">The snippet body, with its comment indentation already removed.</param>
    /// <param name="prologue">The using directives to compile it with.</param>
    /// <param name="references">The metadata references to compile it against.</param>
    /// <returns>
    ///     <see langword="null"/> when some candidate compiles; otherwise a report naming the
    ///     snippet and the best candidate's errors.
    /// </returns>
    private static string? TryCompile(string snippet, string prologue, List<MetadataReference> references)
    {
        // Three shapes a reader may reasonably have written: a whole file, a member declaration, or
        // a sequence of statements. The method body is declared async so that a snippet may await
        // without the wrapping itself being the reason it fails.
        string[] candidates =
        [
            prologue + snippet,
            prologue + "internal class __Wrap\n{\n" + snippet + "\n}\n",
            prologue + "internal class __Wrap\n{\n    private async Task __M()\n    {\n"
                + snippet + "\n    }\n}\n"
        ];

        List<Diagnostic>? best = null;

        foreach (var candidate in candidates)
        {
            var errors = CompileErrors(candidate, references);
            if (errors.Count == 0)
            {
                return null;
            }

            // Keep the closest fit, because the diagnostics of a wrapping that never suited the
            // snippet describe the wrapping rather than the defect.
            if (best is null || errors.Count < best.Count)
            {
                best = errors;
            }
        }

        var reported = string.Join(
            Environment.NewLine,
            best!.Select(error => "    " + error.GetMessage(CultureInfo.InvariantCulture)));

        return Environment.NewLine
               + "--- example ---" + Environment.NewLine
               + snippet + Environment.NewLine
               + "--- errors ---" + Environment.NewLine
               + reported;
    }

    /// <summary>
    ///     Compiles one candidate and returns its errors.
    /// </summary>
    /// <remarks>
    ///     Only errors are collected. Warnings such as an unused local are normal in an
    ///     illustrative snippet and say nothing about whether the API it shows is real.
    /// </remarks>
    /// <param name="source">The complete candidate source text.</param>
    /// <param name="references">The metadata references to compile against.</param>
    /// <returns>The error diagnostics, empty when the candidate compiles.</returns>
    private static List<Diagnostic> CompileErrors(string source, List<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            "__XmlDocExampleVerification",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return [.. compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}
