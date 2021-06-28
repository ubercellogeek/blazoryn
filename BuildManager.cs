using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Tasks;
using blazoryn.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace blazoryn
{
    public class BuildManager
    {
        public const string DefaultCode = @"using System;

class Program
{
    public static void Main()
    {
        Console.WriteLine(""Hello World"");
    }
}";
        private readonly HttpClient _client;
        public EventCallback<BuildManagerState> OnStateChanged { get; set; }
        private const string _frameworkFolder = "_framework/";
        private const string _bootFile = "_framework/blazor.boot.json";
        private BuildManagerState _internalState = new BuildManagerState();
        private List<MetadataReference> _references = new List<MetadataReference>();

        public BuildManagerState State { get { return _internalState; } }

        public BuildManager(HttpClient client)
        {
            _client = client;
        }

        public async Task Initialize()
        {
            _internalState.State = BuildManagerStateType.Initializing;
            _internalState.Message = "Downloading blazor framwork metadata";
            await OnStateChanged.InvokeAsync(_internalState);
            var metaResponse = await _client.GetFromJsonAsync<BlazorBootMetadata>(_bootFile);
            var dlls = metaResponse.Resources.Assembly.Select(x=>x.Key).ToList();

            for (int i = 0; i < dlls.Count; i++)
            {
                try
                {
                    _internalState.Message = $"{dlls[i]}";
                    _internalState.PercentComplete = (int)((double)i/(double)dlls.Count * (double)100);
                    
                    await OnStateChanged.InvokeAsync(_internalState);

                    var response = await _client.GetAsync($"{_frameworkFolder}{dlls[i]}");

                    using (var task = await response.Content.ReadAsStreamAsync())
                    {
                        _references.Add(MetadataReference.CreateFromStream(task));
                    }
                    await Task.Delay(1);
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            _internalState.Message = "Initializing Roslyn for WASM workloads...";
            await OnStateChanged.InvokeAsync(_internalState);
            await Task.Delay(10);
            _internalState.Message = "Initializing Roslyn for WASM workloads...";
            await OnStateChanged.InvokeAsync(_internalState);
            await Task.Delay(100);

            // The first build breaks because at this time Blazor WASM doesn't support mutext (which Roslyn relies on underneath the hood). 
            // Subsequent executions should succeed without issue.
            // https://github.com/dotnet/runtime/issues/43411
            try
            {
                InternalBuild(DefaultCode);
            }
            catch (System.PlatformNotSupportedException) {}


            _internalState.Reset();
            _internalState.State = BuildManagerStateType.Idle;
            await OnStateChanged.InvokeAsync(_internalState);
        }

        private (byte[] ilBytes, ImmutableArray<Diagnostic> logs) InternalBuild(string source)
        {
            var compilation = CSharpCompilation.Create("DynamicCode")
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
                .AddReferences(_references)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)));

            using var assemblyStream = new MemoryStream();
            var emitResult = compilation.Emit(assemblyStream);
            return (assemblyStream.ToArray(), emitResult.Diagnostics);      
        }

        private BuildResult Build(string source)
        {
            var result = new BuildResult();
            var stw = new Stopwatch();
            stw.Start();
            
            try
            {
                var buildResult = InternalBuild(source);

                result.Success = buildResult.logs.Any(x=>x.Severity == DiagnosticSeverity.Error) ? false : true;
                result.ILBytes = buildResult.ilBytes;
                result.Logs =  buildResult.logs;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Exception = ex;
            }
            finally
            {
                stw.Stop();
                result.Duration = stw.Elapsed;
            }
            
            return result;
        }

        private async Task<ExecutionResult> Run(byte[] ilBytes)
        {
            var result = new ExecutionResult();
            var stw = new Stopwatch();
            var initialConsole = Console.Out;
            Assembly asm;
            stw.Start();

            // Load assembly
            try
            {
                asm = Assembly.Load(ilBytes);
            }
            catch (System.Exception asmLoadEx)
            {
                result.Exception = asmLoadEx;
                stw.Stop();
                return result;
            }

            // Run
            try
            {
                 var writer = new StringWriter();
                 Console.SetOut(writer);

                var entry = asm.EntryPoint;
                if (entry.Name == "<Main>" || entry.Name == "Main") // sync wrapper over async Task Main
                {
                    entry = entry.DeclaringType.GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); // reflect for the async Task Main

                    var hasArgs = entry.GetParameters().Length > 0;
                    var invokeResult = entry.Invoke(null, hasArgs ? new object[] { new string[0] } : null);
                    if (invokeResult is Task t)
                    {
                        await t;
                    }

                    result.Output = writer.ToString();
                }
                else
                {
                    throw new Exception("Class must have an invokable method named 'Main'");
                }
            }
            catch (System.Exception ex)
            {
                result.Exception = ex;
            }
            finally
            {
                stw.Stop();
                result.Duration = stw.Elapsed;
                Console.SetOut(initialConsole);
            }
           
            return result;
        }

        public async Task<(BuildResult, ExecutionResult)> BuildAndRun(string source)
        {
            _internalState.Message = "Building";
            _internalState.State = BuildManagerStateType.Building;
            _internalState.PercentComplete = 0;
            await OnStateChanged.InvokeAsync(_internalState);
            await Task.Delay(10);
            var buildResult = Build(source);
            var execResult = new ExecutionResult();

            if(buildResult.Success)
            {
                _internalState.Message = "Executing";
                _internalState.State = BuildManagerStateType.Executing;
                _internalState.PercentComplete = 0;
                await OnStateChanged.InvokeAsync(_internalState);
                await Task.Delay(10);
                execResult = await Run(buildResult.ILBytes);

                _internalState.Reset();
                _internalState.State = BuildManagerStateType.Idle;
                await OnStateChanged.InvokeAsync(_internalState);
                await Task.Delay(10);

               return (buildResult, execResult);
            }
            else
            {   
                _internalState.Reset();
                _internalState.State = BuildManagerStateType.Idle;
                await OnStateChanged.InvokeAsync(_internalState);
                await Task.Delay(10);
                return (buildResult, null);
            }
        }
    }
}