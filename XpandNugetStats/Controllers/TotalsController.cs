using System;
using System.Linq;
using System.Management.Automation;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Fasterflect;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using XpandNugetStats.Models;
using XpandPwsh.Cmdlets.Nuget;

namespace XpandNugetStats.Controllers{
    public class NugetPackage{
        public string Id{ get; set; }
        public string Version{ get; set; }
    }
    [Route("api/[controller]")]
    [ApiController]
    public class TotalsController : ControllerBase{
        private static readonly int _absoluteExpirationRelativeToNow=15;
        static readonly Func<int,TimeSpan> Time=i => TimeSpan.FromMinutes(i);

        private readonly IMemoryCache _memoryCache;

        public TotalsController(IMemoryCache memoryCache){
            _memoryCache = memoryCache;
        }

        [HttpGet("packages")]
        public  Task<NugetPackage[]> Packages(XpandPackageSource packageSource=XpandPackageSource.Xpand){
            return _memoryCache.GetOrCreate($"{nameof(Packages)}{packageSource}",entry => Observable.Return(FindXpandNugetPackage.GetPackages(packageSource, 
                            "https://xpandnugetserver.azurewebsites.net/nuget", "https://api.nuget.org/v3/index.json", XpandPackageFilter.All)
                    .Select(o => new NugetPackage {
                        Id = o.BaseObject.GetPropertyValue("Id").ToString(),
                        Version = o.BaseObject.GetPropertyValue("Version").ToString()
                    })
                    .ToEnumerable().ToArray())
                ,  _absoluteExpirationRelativeToNow, Time)
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
        public ActionResult ClearCache(string id){
            _memoryCache.ClearCache();
            return Ok();
        }

        private  Task<Shields> GetShields(string id,bool latest=false,Version version=null){
            
            return _memoryCache.GetOrCreate($"{id}|{latest}|{version}",  entry => {
                return Observable.Defer(() => {
                    var packages = Packages().ToObservable()
                        .SelectMany(_ => _.Select(package => package.Id)).Where(package => Match(package, id)).ToEnumerable().ToArray();
                    return GetNugetPackageDownloadsCount.Downloads(packages, latest, version)
                        .Select(i => new Shields{Message = i.ToMetric(false, true, 1)});

                });
            },  _absoluteExpirationRelativeToNow, Time).ToTask();
        }

        [HttpGet("{id}")]
        public async Task<Shields> Get(string id){
            return await GetShields(id);
        }
    }
}