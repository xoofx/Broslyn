using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynBuddy
{
    /// <summary>
    /// Frontend class to capture the build of a project/solution that contains CSharp projects.
    /// </summary>
    public sealed class CSharpCompilationCapture
    {
        private readonly Dictionary<string, AssemblyMetadata> _references;

        private CSharpCompilationCapture()
        {
            _references = new Dictionary<string, AssemblyMetadata>();
        }

        /// <summary>
        /// Build the specified project or solution to collect the CSharp compilations and allow to reconstruct in memory an inspect-able Roslyn workspace.
        /// </summary>
        /// <param name="projectFileOrSolution">The path to a project or solution.</param>
        /// <param name="configuration">The default configuration to build. Default: Debug.</param>
        /// <param name="platform">The default configuration to build. Default: Any CPU.</param>
        /// <param name="properties">Optional additional properties to pass to the build.</param>
        /// <returns>The result of the compilation capture. Use <see cref="CSharpCompilationCaptureResult.Workspace"/> to get an access to a Roslyn Workspace.</returns>
        public static CSharpCompilationCaptureResult Build(string projectFileOrSolution, string configuration = "Debug", string platform = "Any CPU", Dictionary<string, string> properties = null)
        {
            if (projectFileOrSolution == null) throw new ArgumentNullException(nameof(projectFileOrSolution));
            if (!File.Exists(projectFileOrSolution)) throw new ArgumentException($"Invalid file path argument `{projectFileOrSolution}`. The file path does not exists", nameof(projectFileOrSolution));

            // Create our merged properties used later by msbuild
            var mergedProperties = new Dictionary<string, string> {{"Configuration", configuration}, {"Platform", platform}};
            // Allow passed properties to override configuration/platform
            if (properties != null)
            {
                foreach (var property in properties)
                {
                    mergedProperties[property.Key] = property.Value;
                }
            }

            var inspector = new CSharpCompilationCapture();
            return inspector.Build(projectFileOrSolution, mergedProperties);
        }

        private CSharpCompilationCaptureResult Build(string projectFileOrSolution, Dictionary<string, string> properties)
        {
            // Create a bin log
            var tempBinLog = BuildBinLog(projectFileOrSolution, properties);

            // Read compiler invocations from the log
            var invocations = Microsoft.Build.Logging.StructuredLogger.CompilerInvocationsReader.ReadInvocations(tempBinLog);
            File.Delete(tempBinLog);

            // Create the workspace for all the projects
            var workspace = new AdhocWorkspace();
            var projectToArgs = new Dictionary<ProjectId, CSharpCommandLineArguments>();
            
            foreach (var compilerInvocation in invocations)
            {
                // We support only CSharp projects
                if (compilerInvocation.Language != Microsoft.Build.Logging.StructuredLogger.CompilerInvocation.CSharp)
                {
                    continue;
                }

                // Rebuild command line arguments
                // Try to discard the exec name by going straight to the first csc parameter (assuming that we are using / instead of -)
                // TODO: very brittle, issue opened on StructuredLogger https://github.com/KirillOsenkov/MSBuildStructuredLog/issues/413
                var commandLineArgs = compilerInvocation.CommandLineArguments;
                var indexOfFirstOption = commandLineArgs.IndexOf(" /", StringComparison.Ordinal);
                if (indexOfFirstOption >= 0)
                {
                    commandLineArgs = commandLineArgs.Substring(indexOfFirstOption + 1);
                }
                var args = ParseArguments(commandLineArgs);

                var projectName = Path.GetFileNameWithoutExtension(compilerInvocation.ProjectFilePath);
                var project = workspace.AddProject(projectName, LanguageNames.CSharp);

                project = GetCompilationOptionsAndFiles(project, compilerInvocation, args, out var csharpArguments);

                projectToArgs[project.Id] = csharpArguments;

                workspace.TryApplyChanges(project.Solution);
            }

            return new CSharpCompilationCaptureResult(workspace, projectToArgs);
        }

        /// <summary>
        /// Launch a dotnet msbuild /t:rebuild with the specified project and options
        /// </summary>
        /// <returns>The path to the binary log of msbuild.</returns>
        private static string BuildBinLog(string projectFileOrSolution, Dictionary<string, string> properties)
        {
            var tempBinlogFilePath = Path.GetTempFileName() + ".binlog";

            bool success = false;
            string errorMessage = null;

            Exception innerException = null;
            try
            {
                var argsBuilder = new StringBuilder($"msbuild /t:rebuild /binaryLogger:{EscapeValue(tempBinlogFilePath)} /verbosity:minimal /nologo {EscapeValue(projectFileOrSolution)}");
                // Pass all our user properties to msbuild
                foreach (var property in properties)
                {
                    argsBuilder.Append($" /p:{property.Key}={EscapeValue(property.Value)}");
                }

                var startInfo = new ProcessStartInfo("dotnet", argsBuilder.ToString())
                {
                    WorkingDirectory = Path.GetDirectoryName(projectFileOrSolution),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var process = new Process() {StartInfo = startInfo};

                var output = new StringBuilder();
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        output.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        output.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    errorMessage = $"Unable to build {projectFileOrSolution}. Reason:\n{output}";
                }
                else
                {
                    success = true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected exceptions when trying to build {projectFileOrSolution}. Reason:\n{ex}";
                innerException = ex;
            }

            if (!success)
            {
                File.Delete(tempBinlogFilePath);
                if (innerException != null)
                    throw new InvalidOperationException(errorMessage, innerException);
                throw new InvalidOperationException(errorMessage);
            }

            return tempBinlogFilePath;
        }

        /// <summary>
        /// Creates a Project and collect CSharp arguments from a compiler invocation.
        /// </summary>
        private Project GetCompilationOptionsAndFiles(Project project, Microsoft.Build.Logging.StructuredLogger.CompilerInvocation invocation, List<string> args, out CSharpCommandLineArguments arguments)
        {
            arguments = CSharpCommandLineParser.Default.Parse(args, invocation.ProjectDirectory, sdkDirectory: null);
            
            project = project.WithCompilationOptions(arguments.CompilationOptions);
            project = project.WithParseOptions(arguments.ParseOptions);
            project = project.WithMetadataReferences(arguments.MetadataReferences.Select(x => x.Reference).Select(GetOrCreateMetaDataReference));

            foreach (var sourceFile in arguments.SourceFiles)
            {
                var filePath = sourceFile.Path;
                var name = filePath;
                if (name.StartsWith(invocation.ProjectDirectory))
                {
                    name = name.Substring(invocation.ProjectDirectory.Length);
                }
                var document = project.AddDocument(name, File.ReadAllText(filePath), filePath: filePath);
                project = document.Project;
            }

            return project;
        }

        /// <summary>
        /// Cache metadata references to avoid loading them between projects
        /// </summary>
        private MetadataReference GetOrCreateMetaDataReference(string file)
        {
            if (!_references.TryGetValue(file, out var meta))
            {
                meta = AssemblyMetadata.CreateFromFile(file);
                _references.Add(file, meta);
            }
            return meta.GetReference(filePath: file);
        }

        /// <summary>
        /// Collect compiler arguments in a list from the specified string.
        /// </summary>
        private static List<string> ParseArguments(string commandLineArguments)
        {
            var args = new List<string>();
            var arg = new StringBuilder();
            for (var i = 0; i < commandLineArguments.Length; i++)
            {
                var c = commandLineArguments[i];
                if (char.IsWhiteSpace(c)) continue;
                arg.Length = 0;
                for(; i < commandLineArguments.Length; i++)
                {
                    c = commandLineArguments[i];
                    if (c == '"')
                    {
                        bool previousIsEscape = false;
                        bool argFlush = false;
                        arg.Append('"');
                        for (i++; i < commandLineArguments.Length; i++)
                        {
                            c = commandLineArguments[i];
                            arg.Append(c);
                            if (!previousIsEscape && c == '"')
                            {
                                argFlush = true;
                                break;
                            }

                            if (!previousIsEscape && c == '\\')
                            {
                                previousIsEscape = true;
                            }
                            else
                            {
                                previousIsEscape = false;
                            }
                        }

                        if (argFlush) break;
                        throw new InvalidOperationException($"Invalid string `{arg}` non terminated by a closing `\"`");
                    }
                    else if (char.IsWhiteSpace(c))
                    {
                        break;
                    }
                    else
                    {
                        arg.Append(c);
                    }
                }

                args.Add(arg.ToString());
            }
            return args;
        }

        private static readonly Regex MatchWhitespace = new Regex(@"[\s\:]");

        private static string EscapeValue(string path)
        {
            path = path.Replace("\"", "\\\"");
            return MatchWhitespace.IsMatch(path) ? $"\"{path}\"" : path;
        }
    }
}