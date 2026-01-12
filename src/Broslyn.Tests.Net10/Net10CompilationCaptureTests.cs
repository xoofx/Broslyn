using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Broslyn.Tests.Net10;

/// <summary>
/// Testing <see cref="CSharpCompilationCapture"/> with .NET 10 projects
/// </summary>
public class Net10CompilationCaptureTests
{
    [Test]
    public async Task TestLibraryNet10()
    {
        var projectPath = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "TestProjects", "TestLibraryNet10.sln");

        var clock = Stopwatch.StartNew();
        var result = CSharpCompilationCapture.Build(projectPath);
        Console.WriteLine($"Project {projectPath} built in {clock.Elapsed.TotalMilliseconds}ms");

        var workspace = result.Workspace;

        // TestLibraryNet10 (net10.0)
        Assert.That(workspace.CurrentSolution.Projects.Count(), Is.EqualTo(1));

        Assert.That(workspace.CurrentSolution.Projects.Any(p => p.Name == "TestLibraryNet10"), Is.True, "Expecting TestLibraryNet10 in the projects");

        foreach (var project in workspace.CurrentSolution.Projects)
        {
            clock.Restart();
            Assert.That(project.CompilationOptions?.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));

            var hasArguments = result.TryGetCommandLineArguments(project, out var arguments);
            Assert.That(hasArguments, Is.True, "Unable to retrieve CSharp Arguments by Project");
            hasArguments = result.TryGetCommandLineArguments(project.Id, out arguments);
            Assert.That(hasArguments, Is.True, "Unable to retrieve CSharp Arguments by ProjectId");

            // Compile the project
            var compilation = await project.GetCompilationAsync();
            Assert.That(compilation, Is.Not.Null, "Compilation must not be null");
            var diagnostics = compilation!.GetDiagnostics();

            var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                var builder = new StringBuilder();
                builder.AppendLine($"Project {project.Name} has errors:");
                foreach (var diag in errors)
                {
                    builder.AppendLine($"{diag}");
                }
                Assert.Fail($"Invalid compilation errors: {builder}");
            }
            else
            {
                Console.WriteLine($"Project {project.Name} ({project.CompilationOptions?.OptimizationLevel}) in-memory compiled in {clock.Elapsed.TotalMilliseconds}ms");

                // We should have at least 1 syntax tree
                var trees = compilation.SyntaxTrees.ToList();
                Assert.That(trees.Count, Is.Not.EqualTo(0), "Expecting SyntaxTrees");
            }
        }
    }
}
