using System;
using System.Linq;
using System.Management.Automation;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using WebApplication3.Models;
using XpandPosh.Cmdlets.Nuget;

namespace XpandNugetStats.Controllers{
    [Route("api/[controller]")]
    [ApiController]
    public class TotalsController : ControllerBase{
        private readonly IMemoryCache _memoryCache;
        private static readonly string[] Packages;

        static TotalsController(){
            Packages = FindXpandNugetPackage.GetPackages(XpandPackageSource.Xpand,
                    "https://xpandnugetserver.azurewebsites.net/nuget", null,XpandPackageFilter.All).Replay().RefCount().ToEnumerable().ToArray();
        }
        public TotalsController(IMemoryCache memoryCache){
            _memoryCache = memoryCache;
        }

        private async Task<Shields> UpdateDownloads(string id,bool latest=false,Version version=null){
            var packages = Packages.Where(package => Match(package,id));
            var downloads = await GetNugetPackageDownloadsCount.Downloads(packages,latest,version);
            var cacheEntryOptions = new MemoryCacheEntryOptions();
            cacheEntryOptions.RegisterPostEvictionCallback(async (key, o, reason, state) => await UpdateDownloads(id,latest,version));
            cacheEntryOptions.AddExpirationToken(
                new CancellationChangeToken(new CancellationTokenSource(TimeSpan.FromMinutes(10)).Token));
            var shields = new Shields{Message = downloads.ToMetric(false, true, 1)};
            _memoryCache.Set($"{id}|{latest}|{version}", shields, cacheEntryOptions);
            return shields;
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

        private async Task<Shields> GetShields(string id,bool latest=false,Version version=null){
            return await _memoryCache.GetOrCreateAsync($"{id}|{latest}|{version}", async entry => {
                var updateDownloads = await UpdateDownloads(id,latest,version);
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                return updateDownloads;
            });
        }

        [HttpGet("{id}")]
        public async Task<Shields> Get(string id){
            return await GetShields(id);
        }
    }
}