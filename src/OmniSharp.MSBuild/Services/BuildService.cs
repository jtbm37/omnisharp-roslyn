
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

namespace OmniSharp.MSBuild.Services
{
    [OmniSharpHandler(OmnisharpEndpoints.BuildProject, LanguageNames.CSharp)]
    public class BuildService : RequestHandler<BuildRequest, BuildResponse>
    {

        IEnumerable<IProjectSystem> _projectSystems;
        private readonly ILogger _logger;


        [ImportingConstructor]
        public BuildService([ImportMany] IEnumerable<IProjectSystem> projectSystem, ILoggerFactory loggerFactory)
        {
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
                    response.Success = prj.Build();
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
}
