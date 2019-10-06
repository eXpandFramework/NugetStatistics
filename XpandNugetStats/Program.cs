using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using WebApplication3;
using XpandPwsh.Cmdlets.Nuget.GetNugetPackageSearchMetadata;

namespace XpandNugetStats
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
