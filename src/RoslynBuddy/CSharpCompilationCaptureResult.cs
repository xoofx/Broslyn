using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynBuddy
{
    /// <summary>
    /// Result of a CSharp compilation capture that provides access to a Roslyn Workspace to allow in memory inspection.
    /// </summary>
    public sealed class CSharpCompilationCaptureResult
    {
        private readonly Dictionary<ProjectId, CSharpCommandLineArguments> _commandLineArguments;
        
        internal CSharpCompilationCaptureResult(AdhocWorkspace workspace, Dictionary<ProjectId, CSharpCommandLineArguments> commandLineArguments)
        {
            _commandLineArguments = commandLineArguments;
            Workspace = workspace;
        }

        /// <summary>
        /// Gets the associated workspace.
        /// </summary>
        public AdhocWorkspace Workspace { get; }

        /// <summary>
        /// Tries to get the associated <see cref="CSharpCommandLineArguments"/> to the specified project id.
        /// </summary>
        /// <param name="projectId">The project id.</param>
        /// <param name="commandLineArguments">The output arguments if it was found.</param>
        /// <returns><c>true</c> if the project id has associated command line arguments.</returns>
        public bool TryGetCommandLineArguments(ProjectId projectId, out CSharpCommandLineArguments commandLineArguments)
        {
            return _commandLineArguments.TryGetValue(projectId, out commandLineArguments);
        }

        /// <summary>
        /// Tries to get the associated <see cref="CSharpCommandLineArguments"/> to the specified project.
        /// </summary>
        /// <param name="project">The project</param>
        /// <param name="commandLineArguments">The output arguments if it was found.</param>
        /// <returns><c>true</c> if the project id has associated command line arguments.</returns>
        public bool TryGetCommandLineArguments(Project project, out CSharpCommandLineArguments commandLineArguments)
        {
            return TryGetCommandLineArguments(project.Id, out commandLineArguments);
        }
    }
}