using Newtonsoft.Json;

namespace LineBotFunctions.Models;
public class LineStatus
{
    [JsonProperty("replyToken")]
    public string ReplyToken { get; set; }
}
