using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arb.Core.Infrastructure.External.Polymarket
{
    public class PolymarketClobOptions
    {
        public const string SectionName = "PolymarketClob";

        public string BaseUrl { get; init; } = "https://clob.polymarket.com";

        // Timeout por requisição em milissegundos
        public int RequestTimeoutMs { get; init; } = 5000;

        // Número de tentativas em erros transitórios
        public int RetryCount { get; init; } = 3;

        // Delay base do backoff exponencial em milissegundos
        // Tentativa 1: 500ms, Tentativa 2: 1000ms, Tentativa 3: 2000ms
        public int RetryBaseDelayMs { get; init; } = 500;
    }
}
