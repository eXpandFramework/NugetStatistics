using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Fasterflect;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using XpandNugetStats.Models;
using XpandPwsh.Cmdlets.Nuget;

namespace XpandNugetStats.Controllers{
    
    [Route("api/[controller]")]
    [ApiController]
    public class TotalsController : ControllerBase{
        private const int BuildPipelineId = 23;
        private const int TestPipelineId = 69;
        readonly Dictionary<string,int> _definitions=new Dictionary<string, int>(){{"build",BuildPipelineId},{"coverage",TestPipelineId},{"tests",BuildPipelineId}};
        private const string NugetFeed = "https://api.nuget.org/v3/index.json";
        private const string XpandFeed = "https://xpandnugetserver.azurewebsites.net/nuget";
        private const int AbsoluteExpirationRelativeToNow = 150;
        static readonly Func<int,TimeSpan> Time=i => TimeSpan.FromMinutes(i);

        private readonly IMemoryCache _memoryCache;
        private readonly IConfiguration _configuration;
        private readonly VssConnection _connection;

        public TotalsController(IMemoryCache memoryCache,IConfiguration configuration,AzConnection  connection){
            _memoryCache = memoryCache;
            _configuration = configuration;
            _connection = connection.Instance;
        }

        [HttpGet("LatestXAFMinors")]
        public Task<Shields> LatestXAFMinors(int index){
            var xafBuild =  XAFBuild(index, "master", "tests").ToObservable();
            return  LatestXAFMinorsCore(index)
                .ToShields("Version")
                .Zip(xafBuild, (minor, build) => {
                    minor.Color = build.Color;
                    return minor;
                })
                .ToTask();
        }

        private IObservable<Version> LatestXAFMinorsCore(int index){
            return _memoryCache.GetOrCreate($"{nameof(LatestXAFMinors)}{index}", entry => {
                var dxFeed = _configuration["DXFeed"];
                var latestMinors = NugetCmdlet.Providers.GetLatestMinors(dxFeed, "DevExpress.ExpressApp");
                var minors = latestMinors.ToObservable()
                    .SelectMany(versions => versions);

                return (index > 0 ? minors.Take(index) : minors);
            },  AbsoluteExpirationRelativeToNow, Time);
        }

        [HttpGet("XAFBuild")]
        public  Task<Shields> XAFBuild(int index,string branch,string shield){
            shield = shield.ToLower();
            var latestXAFMinorsCore = LatestXAFMinorsCore(index);
            return _memoryCache.GetOrCreate($"{nameof(XAFBuild)}{index}{branch}{shield}", entry => {
                return _connection.GetClient<ProjectHttpClient>().GetProjects().ToObservable()
                    .Select((list,i) => list.First()).FirstAsync()
                    .SelectMany(project => {
                        var definitionId = _definitions[shield];
                        return latestXAFMinorsCore.FirstAsync()
                            .SelectMany(latestXAFMinor => {
                                var buildHttpClient = _connection.GetClient<BuildHttpClient>();
                                return GetBuilds(branch, buildHttpClient, project, definitionId, latestXAFMinor, 1).SelectMany(list => list)
                                    .SelectMany(build => shield == "build" ? Observable.Return(build.Result.ToString()).Cast<object>() :
                                        shield == "coverage" ? Coverage(_connection, project, build) :
                                        Tests(branch, buildHttpClient, project, latestXAFMinor, build))
                                    .ToShields(shield)
                                    .Do(shields => { shields.Color = GetShieldColor(shield, shields); });
                            } ).Publish().RefCount();
                    
                    });
            },  AbsoluteExpirationRelativeToNow, Time).ToTask();


            
        }

        private static string GetShieldColor(string shield, Shields shields){
            if (shield == "build"){
                if (shields.Message != BuildResult.Succeeded.ToString()){
                    return "Red";
                }
            }
            else if (shield == "coverage"){
                var coverage = int.Parse(shields.Message.Replace("%", ""));
                if (coverage < 70){
                    return "Yellow";
                }
                else if (coverage < 50){
                    return "Red";
                }
            }
            else{
                if (!shields.Message.Contains("Failed: 0")){
                    return "Red";
                }
            }

            return "Green";
        }

        private IObservable<object> Tests(string branch, BuildHttpClient buildHttpClient, TeamProjectReference project,Version latestXAFMinor, Build build){
            return GetBuilds(branch, buildHttpClient, project, TestPipelineId, latestXAFMinor)
                .FirstOrDefaultAsync(_ => {
                    return _.Any(build1 => {
                        var deserializeObject = JsonConvert.DeserializeObject<dynamic>(build1.Parameters);
                        var dxPipelineBuildId = deserializeObject.DxPipelineBuildId;
                        return dxPipelineBuildId == build.Id;
                    });
                })
                .SelectMany(_ => {
                    if (_ == null){
                        return Observable.Return("Build=Failed");
                    }

                    return _.ToObservable().SelectMany(build1 => {
                        var client = _connection.GetClient<TestResultsHttpClient>();
                        return client.GetTestRunsAsync(project.Id, build1.Uri.ToString()).ToObservable()
                            .Select(runs =>
                                (Passed: runs.Sum(run => run.PassedTests),
                                    Failed: runs.Sum(run => run.UnanalyzedTests)))
                            .Select(tuple => $"Passed: {tuple.Passed}, Failed: {tuple.Failed}");
                    });

                });
        }

        private static IObservable<List<Build>> GetBuilds(string branch, BuildHttpClient buildHttpClient,
            TeamProjectReference project, int definitionId, Version latestXAFMinor, int? top=null){
            return buildHttpClient
                .GetBuildsAsync(project.Id, new[]{definitionId}, tagFilters: new[]{$"{latestXAFMinor}"},branchName:$"refs/heads/{branch}",top:top)
                .ToObservable();
        }

        private static IObservable<string> Coverage(VssConnection connection, TeamProjectReference project, Build build){
            return connection.GetClient<TestResultsHttpClient>()
                .GetBuildCodeCoverageAsync(project.Id, build.Id, 7).ToObservable()
                .Select(list => {
                    var blockCount = list.Sum(coverage => coverage.Modules.GroupBy(_ => _.Name).Select(grouping => grouping.First()).Sum(moduleCoverage =>moduleCoverage.BlockCount ));
                    var blocksCovered = list.Sum(coverage => coverage.Modules.GroupBy(_ => _.Name).Select(grouping => grouping.First()).Sum(moduleCoverage =>moduleCoverage.Statistics.BlocksCovered ));
                    return $"{(blocksCovered*100/blockCount)}%";
                });
        }

        [HttpGet("packages")]
        public  Task<NugetPackage[]> Packages(XpandPackageSource packageSource=XpandPackageSource.Xpand){
            
            return _memoryCache.GetOrCreate($"{nameof(Packages)}{packageSource}",entry => Observable.Return(FindXpandNugetPackage.GetPackages(packageSource, 
                            XpandFeed, NugetFeed, XpandPackageFilter.All)
                    .Select(o => new NugetPackage {
                        Id = o.BaseObject.GetPropertyValue("Id").ToString(),
                        Version = o.BaseObject.GetPropertyValue("Version").ToString()
                    })
                    .ToEnumerable().ToArray())
                ,  AbsoluteExpirationRelativeToNow, Time)
                .ToTask();
        }

        private bool Match(string package, string id){
            if (id == "eXpand")
                return package.StartsWith(id);
            if (id == "XAF")
                return package.StartsWith("Xpand");
            var wildcardPattern = new WildcardPattern(id);
            return wildcardPattern.IsMatch(package);
        }

        [HttpGet("version")] 
        public async Task<Shields> GetVersion(string id,string version){
            return await GetShields(id,version:Version.Parse(version));
        }

        [HttpGet("latest")] 
        public async Task<Shields> GetLatest(string id){
            return await GetShields(id,true);
        }

        [HttpGet("clearcache")] 
        public ActionResult ClearCache(){
            _memoryCache.ClearCache();
            return Ok();
        }

        private  Task<Shields> GetShields(string id,bool latest=false,Version version=null){
            
            return _memoryCache.GetOrCreate($"{id}|{latest}|{version}",  entry => {
                return Observable.Defer(() => {
                    var packages = Packages().ToObservable()
                        .SelectMany(_ => _.Select(package => package.Id)).Where(package => Match(package, id)).ToEnumerable().ToArray();
                    return GetNugetPackageDownloadsCount.Downloads(packages, latest, version)
                        .Select(i => i.ToMetric(false, true, 1)).ToShields("Totals");

                });
            },  AbsoluteExpirationRelativeToNow, Time).ToTask();
        }

        [HttpGet("{id}")]
        public async Task<Shields> Get(string id){
            return await GetShields(id);
        }
    }
}