using LineBotFunctions.Models;
using System.Threading.Tasks;

namespace LineBotFunctions.Entities;

public interface IStatusEntity
{
    Task SetLineStatus(LineStatus lineStatus);
    
    Task<LineStatus> GetLineStatus();
}