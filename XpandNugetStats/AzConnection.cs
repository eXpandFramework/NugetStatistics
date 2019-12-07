using System;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace XpandNugetStats{
    public class AzConnection : IDisposable{
        public AzConnection(IConfiguration configuration){
            Instance = new VssConnection(new Uri("https://dev.azure.com/eXpandDevOps"),
                new VssBasicCredential(string.Empty, configuration["AzureToken"]));
        }

        public VssConnection Instance{ get; }

        public void Dispose(){
            Instance?.Dispose();
        }
    }
}