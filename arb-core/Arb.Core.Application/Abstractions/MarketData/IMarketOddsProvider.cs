using Arb.Core.Application.Request;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arb.Core.Application.Abstractions.MarketData
{
    public interface IMarketOddsProvider
    {
        string SourceName { get; }

        Task<IReadOnlyList<RawOddsSnapshot>> FetchAsync(
            MarketFetchRequest request,
            CancellationToken ct);
    }
}
