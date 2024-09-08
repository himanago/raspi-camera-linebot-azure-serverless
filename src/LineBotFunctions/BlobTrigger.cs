using System;
using System.IO;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Azure.Storage;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using LineBotFunctions.Entities;
using System.Threading.Tasks;
using LineBotFunctions.Models;
using System.Collections.Generic;
using LineBotFunctions.Config;
using LineOpenApi.MessagingApi.Api;
using LineOpenApi.MessagingApi.Model;

namespace LineBotFunctions;

public class BlobTrigger
{
    private BlobContainerClient BlobContainerClient { get; }
    private IMessagingApiApiAsync Api { get; }
    private LineBotSettings Settings { get; }

    public BlobTrigger(
        IMessagingApiApiAsync api, 
        BlobContainerClient blobContainerClient,
        LineBotSettings settings)
    {
        BlobContainerClient = blobContainerClient;
        Api = api;
        Settings = settings;
    }

    [FunctionName("BlobTrigger")]
    public async Task Run(
        [BlobTrigger("videos/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob,
        [DurableClient] IDurableEntityClient entityClient,
        string name, ILogger log)
    {
        // ファイル名からLINEユーザーIDを取得
        var lineUserId = name.Split("_")[0];

        // EntityIdを作成
        var entityId = new EntityId(nameof(StatusEntity), lineUserId);

        // Entityの状態を取得
        var entityState = await entityClient.ReadEntityStateAsync<StatusEntity>(entityId);

        // Entityが存在する場合
        if (entityState.EntityExists && !string.IsNullOrEmpty(entityState.EntityState.LineStatus.ReplyToken))
        {
            // 動画を送信し、Entityの状態を更新
            var storageAccountKey = Environment.GetEnvironmentVariable("AzureWebJobsStorage").Split("AccountKey=")[1].Split(";")[0];
            var key = new StorageSharedKeyCredential(BlobContainerClient.AccountName, storageAccountKey);
            var contentUrl = GetBlobSasUri(name, key);

            var replyMessageRequest = new ReplyMessageRequest(entityState.EntityState.LineStatus.ReplyToken, new List<Message>
            {
                new VideoMessage(contentUrl, contentUrl)
            });
            await Api.ReplyMessageAsync(replyMessageRequest);

            // Entity の状態を空に更新
            await entityClient.SignalEntityAsync<IStatusEntity>(entityId, proxy => proxy.SetLineStatus(new LineStatus { ReplyToken = string.Empty }));
        }   
    }

    private string GetBlobSasUri(string blobName, StorageSharedKeyCredential key)
    {
        // Create a SAS token
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = BlobContainerClient.Name,
            BlobName = blobName,
            Resource = "b",
        };

        sasBuilder.StartsOn = DateTimeOffset.UtcNow.AddMinutes(-15);
        sasBuilder.ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10);
        sasBuilder.SetPermissions(BlobContainerSasPermissions.Read);

        // Use the key to get the SAS token.
        var sasToken = sasBuilder.ToSasQueryParameters(key).ToString();

        return $"{BlobContainerClient.GetBlockBlobClient(blobName).Uri}?{sasToken}";
    }
}
