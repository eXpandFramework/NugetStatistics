using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XpandPosh.Cmdlets.Nuget.GetNugetPackageSearchMetadata;

namespace WebApplication3
{
    public class Program
    {
        public static void Main(string[] args){
            
            AppDomain.CurrentDomain.AssemblyResolve+=CurrentDomainOnAssemblyResolve;
            CreateWebHostBuilder(args).Build().Run();
        }
        private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args){
            if (args.Name.Contains("Newton")){
                return Assembly.LoadFile(
                    $@"{Path.GetDirectoryName(typeof(GetNugetPackageSearchMetadata).Assembly.Location)}\Newtonsoft.Json.dll");
            }
            return null;
        }
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();
    }
}
