
using System.Collections.Generic;
using System.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Mef;
using OmniSharp.Models;
using OmniSharp.Models.V2;
using OmniSharp.MSBuild.Helpers;
using OmniSharp.Roslyn;
using OmniSharp.Services;

namespace OmniSharp.MSBuild.Services
{
    [OmniSharpHandler(OmnisharpEndpoints.RunMsTest, LanguageNames.CSharp)]
    public class MsTestService: RequestHandler<MsTestRequest, MsTestResponse>
    {
        IOmnisharpEnvironment _env;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        MsBuildHelper _msbuildhelper;
        StreamWriter _writer;
        List<QuickFix> _quickFixes;
        BuildLogParser _logParser;
        bool _success;
        MetadataHelper _metadataHelper;
        OmnisharpWorkspace _workspace;

        [ImportingConstructor]
        public MsTestService(MsBuildHelper msbuildHelper,
                             IOmnisharpEnvironment env,
                             ILoggerFactory loggerFactory,
                             MetadataHelper metadataHelper,
                             OmnisharpWorkspace workspace)
        {
            _msbuildhelper = msbuildHelper;
            _env = env;
            _logger = loggerFactory.CreateLogger<MsTestService>();
            _metadataHelper = metadataHelper;
            _workspace = workspace;
        }

        public async Task<MsTestResponse> Handle(MsTestRequest request)
        {

            var response = new MsTestResponse();
            _success = true;
            _quickFixes = new List<QuickFix>();
            _logParser = new BuildLogParser();
            var project = await _msbuildhelper.GetFileProject(request.FileName);
            if(project == null)
            {
                return response;
            }

            var solutionPath = System.IO.Path.GetDirectoryName(_env.SolutionFilePath);
            var testLogPath = System.IO.Path.Combine(solutionPath,"mstest.log");
            using(var testLogFile = System.IO.File.Create(testLogPath))
            {
                var projectPath = project.TargetPath;
                var args = $"/testcontainer:{projectPath} /detail:errorstacktrace /detail:errormessage /noresults";
                var testArg = GetTest(request);
                _logger.LogDebug("Running mstest with args: " + args);
                using(_writer = new StreamWriter(testLogFile))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "mstest",
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

            return response;
        }

        private async Task<string> GetTest(MsTestRequest request)
        {
            var args = string.Empty;
            switch(request.Type)
            {
                case MsTestRun.CurrentTest:
                    var currentMethod = await GetCurrentMethod(request);
                    if(!string.IsNullOrEmpty(currentMethod))
                    {
                        args += " /test:" + currentMethod;
                        break;
                    }
                    // intentional no break; if no method found then run the current class tests
                    break;
                case MsTestRun.CurrentClass:
                    var currentClass = await GetCurrentClass(request);
                    if(string.IsNullOrEmpty(currentClass))
                    {
                        return null;
                    }
                    args += " /test:" + currentClass;
                    break;
            }
            return args;
        }

        private SyntaxNode GetTypeDeclaration(SyntaxNode node)
        {
            var type = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();

            if (type == null)
            {
                type = node.SyntaxTree.GetRoot()
                    .DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault();
            }

            return type;
        }

        private async Task<string> GetCurrentMethod(MsTestRequest request)
        {
            var document = _workspace.GetDocument(request.FileName);
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxTree = semanticModel.SyntaxTree;
            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines.GetPosition(new LinePosition(request.Line, request.Column));
            var node = syntaxTree.GetRoot().FindToken(position).Parent;

            SyntaxNode method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if(method == null)
            {
                return null;
            }

            var symbol = semanticModel.GetDeclaredSymbol(method);
            //TODO: Check if method has TestMethod attribute
            return symbol.ToDisplayString().Replace("()", string.Empty);
        }

        private async Task<string> GetCurrentClass(MsTestRequest request)
        {

            var document = _workspace.GetDocument(request.FileName);
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxTree = semanticModel.SyntaxTree;
            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines.GetPosition(new LinePosition(request.Line, request.Column));
            var node = syntaxTree.GetRoot().FindToken(position).Parent;

            SyntaxNode method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            SyntaxNode type = GetTypeDeclaration(node);

            if (type == null)
            {
                return null;
            }

            var symbol = semanticModel.GetDeclaredSymbol(method ?? type);
            return _metadataHelper.GetSymbolName(symbol);
        }


        void OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _writer.WriteLine(e.Data);
            if (e.Data == null)
                return;

            if (e.Data == "Test Run Failed.")
                _success = false;
            var quickfix = _logParser.Parse(e.Data);
            if(quickfix != null)
                _quickFixes.Add(quickfix);
        }

        void ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _writer.WriteLine(e.Data);
        }
    }
}
