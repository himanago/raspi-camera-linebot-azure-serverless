using LineBotFunctions.Models;
using System.Threading.Tasks;

namespace LineBotFunctions.Entities;

public class StatusEntity : IStatusEntity
{
    public LineStatus LineStatus { get; set; }

    public async Task SetLineStatus(LineStatus lineStatus) => LineStatus = lineStatus;

    public async Task<LineStatus> GetLineStatus() => LineStatus;
}