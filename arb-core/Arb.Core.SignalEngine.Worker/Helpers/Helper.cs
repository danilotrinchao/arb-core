using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arb.Core.SignalEngine.Worker.Helpers
{
    public class Helper
    {
       // private async Task AckWithRetryAsync(string messageId, CancellationToken ct)
       // {
       //     for (var attempt = 1; attempt <= 3; attempt++)
       //     {
       //         try
       //         {
       //             await _consumer.AckAsync(_streams.OddsTicks, GroupName, messageId, ct);
       //             return;
       //         }
       //         catch (StackExchange.Redis.RedisTimeoutException ex)
       //         {
       //             _logger.LogWarning(ex, "ACK timeout for msgId={MsgId}, attempt={Attempt}", messageId, attempt);
       //             await Task.Delay(500 * attempt, ct);
       //         }
       //     }
       //
       //     _logger.LogError("ACK failed permanently for msgId={MsgId}", messageId);
       // }
    }
}
