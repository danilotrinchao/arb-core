using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arb.Core.Infrastructure.External.Polymarket
{
    public class PolymarketClobPriceClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly PolymarketClobOptions _options;
        private readonly ILogger<PolymarketClobPriceClient> _logger;

        public PolymarketClobPriceClient(
            HttpClient httpClient,
            IOptions<PolymarketClobOptions> options,
            ILogger<PolymarketClobPriceClient> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = new Uri(_options.BaseUrl);
                _httpClient.Timeout = TimeSpan.FromMilliseconds(_options.RequestTimeoutMs);
            }
        }

        /// <summary>
        /// Consulta o midpoint de um único token.
        /// Retorna null se o token não tiver orderbook ou em caso de erro persistente.
        /// </summary>
        public async Task<decimal?> GetMidpointAsync(
            string tokenId,
            CancellationToken ct)
        {
            var results = await GetMidpointsAsync(new[] { tokenId }, ct);
            return results.GetValueOrDefault(tokenId);
        }

        /// <summary>
        /// Consulta o midpoint de múltiplos tokens em uma única chamada.
        /// Retorna dicionário tokenId → midPrice.
        /// Tokens sem orderbook ou com erro são omitidos do resultado.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, decimal>> GetMidpointsAsync(
            IReadOnlyList<string> tokenIds,
            CancellationToken ct)
        {
            if (tokenIds.Count == 0)
                return new Dictionary<string, decimal>();

            // Constrói query string com múltiplos token_id
            // GET /midpoint?token_id=abc&token_id=def&token_id=ghi
            var query = BuildMidpointQuery(tokenIds);

            for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(query, ct);

                    // 400 ou 404 são erros definitivos — token inválido ou sem orderbook
                    // Não faz sentido tentar de novo
                    if (response.StatusCode == HttpStatusCode.BadRequest ||
                        response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning(
                            "Polymarket CLOB midpoint returned {Status} for {Count} token(s). No retry.",
                            response.StatusCode,
                            tokenIds.Count);

                        return new Dictionary<string, decimal>();
                    }

                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(ct);

                    return ParseMidpointResponse(content, tokenIds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cancelamento explícito — propaga sem retry
                    throw;
                }
                catch (Exception ex) when (attempt < _options.RetryCount)
                {
                    // Erro transitório — aguarda backoff e tenta novamente
                    var delay = TimeSpan.FromMilliseconds(
                        _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

                    _logger.LogWarning(
                        ex,
                        "Polymarket CLOB midpoint attempt {Attempt}/{Max} failed. " +
                        "Retrying in {DelayMs}ms. TokenCount={Count}",
                        attempt,
                        _options.RetryCount,
                        delay.TotalMilliseconds,
                        tokenIds.Count);

                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    // Última tentativa falhou — loga e retorna vazio
                    _logger.LogError(
                        ex,
                        "Polymarket CLOB midpoint failed after {Max} attempt(s). " +
                        "TokenCount={Count}",
                        _options.RetryCount,
                        tokenIds.Count);

                    return new Dictionary<string, decimal>();
                }
            }

            return new Dictionary<string, decimal>();
        }

        private static string BuildMidpointQuery(IReadOnlyList<string> tokenIds)
        {
            // Para um único token: /midpoint?token_id=abc
            // Para múltiplos: /midpoint?token_id=abc&token_id=def
            var queryParts = tokenIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => $"token_id={Uri.EscapeDataString(id)}");

            return "/midpoint?" + string.Join("&", queryParts);
        }

        private IReadOnlyDictionary<string, decimal> ParseMidpointResponse(
            string content,
            IReadOnlyList<string> tokenIds)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(content))
                return result;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (tokenIds.Count == 1)
                {
                    // Resposta para token único: { "mid_price": "0.45" }
                    if (root.TryGetProperty("mid", out var midPriceEl))
                    {
                        var midPriceStr = midPriceEl.GetString();
                        if (decimal.TryParse(
                                midPriceStr,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var midPrice) && midPrice > 0)
                        {
                            result[tokenIds[0]] = midPrice;
                        }
                    }
                }
                else
                {
                    // Resposta para múltiplos tokens — array ou objeto com múltiplas entradas
                    // A API pode retornar formato diferente para múltiplos tokens
                    // Tratamos os dois casos: array e objeto
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            ParseMidpointItem(item, result);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        // Se vier como objeto único com mid_price, associa ao único token
                        if (root.TryGetProperty("mid_price", out var midPriceEl) &&
                            tokenIds.Count == 1)
                        {
                            var midPriceStr = midPriceEl.GetString();
                            if (decimal.TryParse(
                                    midPriceStr,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var midPrice) && midPrice > 0)
                            {
                                result[tokenIds[0]] = midPrice;
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse Polymarket CLOB midpoint response. Content={Content}",
                    content.Length > 200 ? content[..200] : content);
            }

            return result;
        }

        private static void ParseMidpointItem(
            JsonElement item,
            Dictionary<string, decimal> result)
        {
            if (!item.TryGetProperty("asset_id", out var assetIdEl) &&
                !item.TryGetProperty("token_id", out assetIdEl))
                return;

            var tokenId = assetIdEl.GetString();
            if (string.IsNullOrWhiteSpace(tokenId))
                return;

            if (!item.TryGetProperty("mid_price", out var midPriceEl) &&
                !item.TryGetProperty("price", out midPriceEl))
                return;

            var midPriceStr = midPriceEl.GetString();
            if (decimal.TryParse(
                    midPriceStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var midPrice) && midPrice > 0)
            {
                result[tokenId] = midPrice;
            }
        }
    }
}
