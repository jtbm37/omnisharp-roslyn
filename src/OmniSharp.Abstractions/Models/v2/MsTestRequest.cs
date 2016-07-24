

using OmniSharp.Mef;

namespace OmniSharp.Models.V2
{
    [OmniSharpEndpoint(OmnisharpEndpoints.RunMsTest, typeof(MsTestRequest), typeof(MsTestResponse))]
    public class MsTestRequest : Request
    {
        public MsTestRun Type { get; set; }
    }

    public enum MsTestRun
    {
        All,
        CurrentTest,
        CurrentClass
    }
}
