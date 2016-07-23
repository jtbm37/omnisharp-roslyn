using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.V2;
using OmniSharp.Services;
using Project = Microsoft.Build.Evaluation.Project;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OmniSharp.MSBuild.Services
{
    [OmniSharpHandler(OmnisharpEndpoints.BuildProject, LanguageNames.CSharp)]
    public class BuildService : RequestHandler<BuildRequest, BuildResponse>
    {

        IEnumerable<IProjectSystem> _projectSystems;
        IOmnisharpEnvironment _env;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        StreamWriter _writer;
        List<QuickFix> _quickFixes;
        BuildLogParser _logParser;
        bool _success;

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
            _quickFixes = new List<QuickFix>();
            _logParser = new BuildLogParser();
            foreach(var projectSystem in _projectSystems)
            {
                var project = await projectSystem.GetProjectModel(request.FileName);
                if(project is MSBuildProject)
                {
                    var msbuildproject = (MSBuildProject)project;
                    var projectPath = System.IO.Path.GetDirectoryName(_env.SolutionFilePath);
                    var configuration = request.Configuration ?? "Debug";
                    var args = $"{msbuildproject.Path} /p:Configuration={configuration}";
                    if(request.ExcludeProjectReferences)
                    {
                        args += " /p:BuildProjectReferences=false";
                    }
                    var buildLogPath = System.IO.Path.Combine(projectPath,"build.log");
                    var buildLogFile = System.IO.File.Create(buildLogPath);
                    using(_writer = new StreamWriter(buildLogFile))
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "msbuild",
                            Arguments = args,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            RedirectStandardInput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        var process = new Process
                        {
                            StartInfo = startInfo,
                            EnableRaisingEvents = true
                        };

                        process.ErrorDataReceived += ErrorDataReceived;
                        process.OutputDataReceived += OutputDataReceived;
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();
                        response.Quickfixes = _quickFixes;
                        response.Success = _success;
                    }
                }
            }
            return response;
        }

        void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _writer.WriteLine(e.Data);
            if (e.Data == null)
                return;

            if (e.Data == "Build succeeded.")
              _success = true;
            var quickfix = _logParser.Parse(e.Data);
            if(quickfix != null)
                _quickFixes.Add(quickfix);
        }

        void ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            // _logger.Error(e.Data);
            _writer.WriteLine(e.Data);
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

    public class BuildLogParser
    {

        public QuickFix Parse(string line)
        {
            if (!line.Contains("warning CS") && !line.Contains("error CS"))
                return null;

            var match = GetMatches(line, @".*(Source file '(.*)'.*)");
            if(match.Matched)
            {
                var matches = match.Matches;
                var quickFix = new QuickFix
                {
                    FileName = matches[0].Groups[2].Value,
                    Text = matches[0].Groups[1].Value.Replace("'", "''")
                };

                return quickFix;
            }

            match = GetMatches(line, @"\s*(.*cs)\((\d+),(\d+)\).*(warning|error) CS\d+: (.*) \[");
            if(match.Matched)
            {
                var matches = match.Matches;
                var quickFix = new QuickFix
                {
                    FileName = matches[0].Groups[1].Value,
                    Line = int.Parse(matches[0].Groups[2].Value),
                    Column = int.Parse(matches[0].Groups[3].Value),
                    Text = "[" + matches[0].Groups[4].Value + "] " + matches[0].Groups[5].Value.Replace("'", "''")
                };

                return quickFix;
            }
            return null;
        }

        private Match GetMatches(string line, string regex)
        {
            var match = new Match();
            var matches = Regex.Matches(line, regex, RegexOptions.Compiled);
            if (matches.Count > 0 && !Regex.IsMatch(line, @"\d+>"))
            {
                match.Matched = true;
                match.Matches = matches;
            }
            return match;
        }

        class Match
        {
            public bool Matched { get; set; }
            public MatchCollection Matches { get; set; }
        }
    }
}
