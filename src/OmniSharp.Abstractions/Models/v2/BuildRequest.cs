
using OmniSharp.Mef;

namespace OmniSharp.Models.V2
{
    [OmniSharpEndpoint(OmnisharpEndpoints.BuildProject, typeof(BuildRequest), typeof(BuildResponse))]
    public class BuildRequest : Request
    {
        public string Configuration { get; set; }
        public bool ExcludeProjectReferences { get; set; }
        public string Language { get; set; }
    }
}
