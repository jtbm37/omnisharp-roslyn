
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.V2;
using OmniSharp.Services;
using Project = Microsoft.Build.Evaluation.Project;
using System.IO;

namespace OmniSharp.MSBuild.Services
{
    [OmniSharpHandler(OmnisharpEndpoints.BuildProject, LanguageNames.CSharp)]
    public class BuildService : RequestHandler<BuildRequest, BuildResponse>
    {

        IEnumerable<IProjectSystem> _projectSystems;
        IOmnisharpEnvironment _env;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;


        [ImportingConstructor]
        public BuildService([ImportMany] IEnumerable<IProjectSystem> projectSystem, IOmnisharpEnvironment env, ILoggerFactory loggerFactory)
        {
            _env = env;
            _projectSystems = projectSystem;
            _logger = loggerFactory.CreateLogger<BuildService>();
        }


        public async Task<BuildResponse> Handle(BuildRequest request)
        {
            var response = new BuildResponse();
            foreach(var projectSystem in _projectSystems)
            {
                var project = await projectSystem.GetProjectModel(request.FileName);
                if(project is MSBuildProject)
                {
                    var msbuildproject = (MSBuildProject)project;
                    _logger.LogDebug($"Building project {msbuildproject.Path}");
                    var prj = GetProject(msbuildproject.Path);
                    prj.SetGlobalProperty("Configuration", request.Configuration ?? "Debug");
                    if(request.ExcludeProjectReferences)
                    {
                        prj.SetGlobalProperty("BuildProjectReferences", "false");
                    }
                    response.Success = prj.Build(new BuildLogger(LoggerVerbosity.Minimal, System.IO.Path.GetDirectoryName(_env.SolutionFilePath)));
                }
            }
            return response;
        }

        public Project GetProject(string path)
        {
            var loadedProjects = ProjectCollection.GlobalProjectCollection.LoadedProjects;
            if(loadedProjects != null)
            {
                var existingProject = loadedProjects.FirstOrDefault(x => x.FullPath == path);
                if(existingProject != null)
                {
                    return existingProject;
                }
            }
            return new Microsoft.Build.Evaluation.Project(path);
        }


    }

    public class BuildLogger : Microsoft.Build.Framework.ILogger
    {
        StreamWriter _writer;
        public string Parameters { get; set; }
        string _projectPath;

        public LoggerVerbosity Verbosity { get; set; }
        public BuildLogger(LoggerVerbosity verbosity, string projectPath)
        {
            _projectPath = projectPath;
            Verbosity = verbosity;
        }

        public void Initialize(IEventSource eventSource)
        {
            var buildLogPath = System.IO.Path.Combine(_projectPath,"build.log");
            _writer = new StreamWriter(buildLogPath);
            eventSource.MessageRaised += eventSource_MessageRaised;
            eventSource.BuildStarted += eventSource_BuildStarted;
            eventSource.BuildFinished += eventSource_BuildFinished;
            eventSource.ProjectStarted += eventSource_ProjectStarted;
            eventSource.ProjectFinished += eventSource_ProjectFinished;
        }

        void eventSource_ProjectStarted(object sender, ProjectStartedEventArgs e)
        {
            _writer.WriteLine(e.Message);
        }

        void eventSource_ProjectFinished(object sender, ProjectFinishedEventArgs e)
        {
            _writer.WriteLine(e.Message);
        }

        void eventSource_BuildFinished(object sender, BuildFinishedEventArgs e)
        {
            _writer.WriteLine(e.Message);
        }

        void eventSource_BuildStarted(object sender, BuildStartedEventArgs e)
        {
            _writer.WriteLine(e.Message);
        }
        void eventSource_MessageRaised(object sender, BuildMessageEventArgs e)
        {
            // BuildMessageEventArgs adds Importance to BuildEventArgs
            // Let's take account of the verbosity setting we've been passed in deciding whether to log the message
            if ((e.Importance == MessageImportance.High && IsVerbosityAtLeast(LoggerVerbosity.Minimal))
                || (e.Importance == MessageImportance.Normal && IsVerbosityAtLeast(LoggerVerbosity.Normal))
                || (e.Importance == MessageImportance.Low && IsVerbosityAtLeast(LoggerVerbosity.Detailed))
                )
            {
                // WriteLineWithSenderAndMessage(String.Empty, e);
            _writer.WriteLine(e.Message);
            }
        }

        public bool IsVerbosityAtLeast(LoggerVerbosity verb)
        {
            return Verbosity == verb;
        }



        public void Shutdown()
        {
            _writer.Close();
        }
    }

}
