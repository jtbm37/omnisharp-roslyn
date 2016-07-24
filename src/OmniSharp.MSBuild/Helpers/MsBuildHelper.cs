
using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using OmniSharp.Models;
using OmniSharp.Services;

namespace OmniSharp.MSBuild.Helpers
{
    [Export(typeof(MsBuildHelper))]
    public class MsBuildHelper
    {

        IEnumerable<IProjectSystem> _projectSystems;

        [ImportingConstructor]
        public MsBuildHelper([ImportMany] IEnumerable<IProjectSystem> projectSystems)
        {
            _projectSystems = projectSystems;
        }

        public async Task<MSBuildProject> GetFileProject(string filename)
        {
            foreach(var projectSystem in _projectSystems)
            {
                var project = await projectSystem.GetProjectModel(filename);
                if(project is MSBuildProject)
                {
                    return (MSBuildProject)project;
                }
            }
            
            return null;
        }
    
    }
}
