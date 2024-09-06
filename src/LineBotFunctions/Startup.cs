using Azure.Storage.Blobs;
using LineBotFunctions.Config;
using LineOpenApi.MessagingApi.Api;
using LineOpenApi.MessagingApi.Client;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.DurableTask.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

[assembly: FunctionsStartup(typeof(LineBotFunctions.Startup))]
namespace LineBotFunctions;
public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("local.settings.json", true)
            .AddEnvironmentVariables()
            .Build();

        var settings = config.GetSection(nameof(LineBotSettings)).Get<LineBotSettings>();

        var api = new MessagingApiApi(new Configuration
        {
            AccessToken = settings.ChannelAccessToken
        });

        // Blob Storage 用
        var blobServiceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
        var blobContainerClient = blobServiceClient.GetBlobContainerClient("videos");
        builder.Services.AddSingleton(blobContainerClient);

        builder.Services
            .Configure<DurableClientOptions>(options =>
                {
                    options.ConnectionName = "DurableManagementStorage";
                    options.TaskHub = Environment.GetEnvironmentVariable("TaskHubName");
                    options.IsExternalClient = true;
                })
            .AddDurableClientFactory()
            .AddSingleton(settings)
            .AddSingleton<IMessagingApiApiAsync>(api);
    }
}