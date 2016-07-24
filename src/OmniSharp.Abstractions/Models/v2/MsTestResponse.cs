

using System.Collections.Generic;

namespace OmniSharp.Models.V2
{
    public class MsTestResponse
    {
        public bool Success { get; set; }
        public List<QuickFix> Quickfixes { get; set; }
    }

}
