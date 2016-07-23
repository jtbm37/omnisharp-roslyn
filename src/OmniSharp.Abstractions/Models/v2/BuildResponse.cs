
using System.Collections.Generic;

namespace OmniSharp.Models.V2
{
    public class BuildResponse
    {
        public bool Success { get; set; }
        public List<QuickFix> Quickfixes { get; set; }
    }

}
