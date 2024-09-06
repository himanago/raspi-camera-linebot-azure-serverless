using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using LineOpenApi.MessagingApi.Api;
using LineOpenApi.MessagingApi.Model;
using LineOpenApi.Webhook.Model;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.DurableTask.ContextImplementations;
using Microsoft.Azure.WebJobs.Extensions.DurableTask.Options;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using LineBotFunctions.Entities;
using LineBotFunctions.Models;

namespace LineBotFunctions;
public class WebhookEndpoint
{
    private IMessagingApiApiAsync Api { get; }

    public WebhookEndpoint(IMessagingApiApiAsync api)
    {
        Api = api;
    }

    [FunctionName("WebhookEndpoint")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        [DurableClient] IDurableEntityClient durableEntityClient,
        [SignalR(HubName = "video")] IAsyncCollector<SignalRMessage> signalRMessages,
        ILogger log)
    {
        log.LogInformation("C# webhook endpoint function processed a request.");

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var callbackRequest = JsonConvert.DeserializeObject<CallbackRequest>(body);

            foreach (var ev in callbackRequest.Events)
            {
                // echo when receive text message
                if (ev is MessageEvent messageEvent && messageEvent.Message is TextMessageContent textMessageContent)
                {
                    // 送信元のLINEユーザーID
                    var userSource = ev.Source as UserSource;

                    // 待機中かどうかを確認
                    var waiterId = new EntityId(nameof(StatusEntity), userSource.UserId);
                    var waiterState = await durableEntityClient.ReadEntityStateAsync<StatusEntity>(waiterId);

                    if (waiterState.EntityExists && !string.IsNullOrEmpty(waiterState.EntityState.LineStatus.ReplyToken))
                    {
                        if (textMessageContent.Text == "リセット")
                        {
                            // リセットと送ると待機中のエンティティを削除できる
                            await durableEntityClient.SignalEntityAsync<IStatusEntity>(waiterId, proxy => proxy.SetLineStatus(new LineStatus { ReplyToken = string.Empty }));
                            var replyMessageRequest = new ReplyMessageRequest(messageEvent.ReplyToken, new List<Message>
                            {
                                new TextMessage("リセットしました。")
                            });
                            await Api.ReplyMessageAsync(replyMessageRequest);   
                        }
                        else
                        {
                            // 待機中の場合は、リプライトークンをリレー更新して「まってね」と送る
                            var replyMessageRequest = new ReplyMessageRequest(waiterState.EntityState.LineStatus.ReplyToken, new List<Message>
                            {
                                new TextMessage("撮影中だよ。ちょっとまってね。")
                            });
                            await Api.ReplyMessageAsync(replyMessageRequest);   

                            // リプライトークンを入れ替え
                            await durableEntityClient.SignalEntityAsync<IStatusEntity>(waiterId, proxy => proxy.SetLineStatus(new LineStatus { ReplyToken = messageEvent.ReplyToken }));

                            log.LogInformation($"{waiterState.EntityState.LineStatus.ReplyToken}がセットされていたので、{messageEvent.ReplyToken}をあらためてセットしました。");
                        }
                    }
                    else
                    {
                        if (textMessageContent.Text == "撮影して！")
                        {
                            // 待機中でない場合は、新規にReplyTokenを取得し て返信
                            log.LogInformation($"{messageEvent.ReplyToken}をセットしました");

                            // LINEユーザーIDをカメラデバイスに通知する
                            await signalRMessages.AddAsync(new SignalRMessage
                            {
                                Target = "requestVideo",
                                Arguments = new[] { userSource.UserId }
                            });

                            // エンティティにリプライトークンをセット
                            await durableEntityClient.SignalEntityAsync<IStatusEntity>(waiterId, proxy => proxy.SetLineStatus(new LineStatus { ReplyToken = messageEvent.ReplyToken }));
                        }
                        else
                        {
                            // 定型文
                            var replyMessageRequest = new ReplyMessageRequest(messageEvent.ReplyToken, new List<Message>
                            {
                                new TextMessage("撮影してほしいときは、「撮影して！」と送ってね。")
                            });
                            await Api.ReplyMessageAsync(replyMessageRequest);                        
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            log.LogError($"Error: {e.Message}");
            log.LogError($"Error: {e.StackTrace}");
        }
        
        return new OkObjectResult("OK");
    }

    [FunctionName(nameof(StatusEntity))]
    public Task StatusEntityFunction([EntityTrigger] IDurableEntityContext ctx)
        => ctx.DispatchAsync<StatusEntity>();
}
