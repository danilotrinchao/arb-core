using Arb.Core.Application.Request;
using Arb.Core.Contracts.Events;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IOddsNormalizer
    {
        IReadOnlyList<OddsTickV1> Normalize(
            string sourceName,
            IReadOnlyList<RawOddsSnapshot> snapshots,
            DateTime nowUtc);
    }
}
