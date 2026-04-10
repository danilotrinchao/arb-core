using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

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

            // Antes: results.GetValueOrDefault(tokenId) -> retornava 0 quando a chave não existia.
            // Agora: devolve null quando a chave NÃO estiver presente no dicionário,
            // para diferenciar ausência de midpoint de preço real 0.
            return results.TryGetValue(tokenId, out var mid) ? (decimal?)mid : null;
        }

        /// <summary>
        /// Consulta o midpoint de múltiplos tokens.
        /// - Token único: GET /midpoint?token_id=abc → { "mid": "0.45" }
        /// - Múltiplos tokens: POST /midpoints com body {"token_ids": [...]} → [{"token_id": "abc", "mid": "0.45"}]
        /// Retorna dicionário tokenId → midPrice.
        /// Tokens sem orderbook ou com erro são omitidos do resultado.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, decimal>> GetMidpointsAsync(
            IReadOnlyList<string> tokenIds,
            CancellationToken ct)
        {
            if (tokenIds.Count == 0)
                return new Dictionary<string, decimal>();

            // Token único — GET /midpoint?token_id=abc
            if (tokenIds.Count == 1)
            {
                var singleQuery = $"/midpoint?token_id={Uri.EscapeDataString(tokenIds[0])}";

                for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
                {
                    try
                    {
                        using var response = await _httpClient.GetAsync(singleQuery, ct);

                        if (response.StatusCode == HttpStatusCode.BadRequest ||
                            response.StatusCode == HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning(
                                "Polymarket CLOB midpoint returned {Status} for token. No retry.",
                                response.StatusCode);
                            return new Dictionary<string, decimal>();
                        }

                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync(ct);
                        return ParseSingleMidpointResponse(content, tokenIds[0]);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (attempt < _options.RetryCount)
                    {
                        var delay = TimeSpan.FromMilliseconds(
                            _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

                        _logger.LogWarning(ex,
                            "Polymarket CLOB single midpoint attempt {Attempt}/{Max} failed. " +
                            "Retrying in {DelayMs}ms.",
                            attempt,
                            _options.RetryCount,
                            delay.TotalMilliseconds);

                        await Task.Delay(delay, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Polymarket CLOB single midpoint failed after {Max} attempt(s).",
                            _options.RetryCount);
                        return new Dictionary<string, decimal>();
                    }
                }

                return new Dictionary<string, decimal>();
            }

            // Múltiplos tokens — POST /midpoints com body {"token_ids": [...]}
            var validIds = tokenIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            var bodyObj = new { token_ids = validIds };
            var bodyJson = JsonSerializer.Serialize(bodyObj);

            for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
            {
                try
                {
                    using var requestContent = new StringContent(
                        bodyJson,
                        Encoding.UTF8,
                        "application/json");

                    using var response = await _httpClient.PostAsync("/midpoints", requestContent, ct);

                    if (response.StatusCode == HttpStatusCode.BadRequest ||
                        response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning(
                            "Polymarket CLOB batch midpoints returned {Status}. No retry.",
                            response.StatusCode);
                        return new Dictionary<string, decimal>();
                    }

                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync(ct);
                    return ParseBatchMidpointResponse(content);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < _options.RetryCount)
                {
                    var delay = TimeSpan.FromMilliseconds(
                        _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

                    _logger.LogWarning(ex,
                        "Polymarket CLOB batch midpoints attempt {Attempt}/{Max} failed. " +
                        "Retrying in {DelayMs}ms. TokenCount={Count}",
                        attempt,
                        _options.RetryCount,
                        delay.TotalMilliseconds,
                        tokenIds.Count);

                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Polymarket CLOB batch midpoints failed after {Max} attempt(s). " +
                        "TokenCount={Count}",
                        _options.RetryCount,
                        tokenIds.Count);

                    return new Dictionary<string, decimal>();
                }
            }

            return new Dictionary<string, decimal>();
        }

        /// <summary>
        /// Parse da resposta do endpoint singular GET /midpoint
        /// Formato esperado: { "mid": "0.765" }
        /// </summary>
        private IReadOnlyDictionary<string, decimal> ParseSingleMidpointResponse(
            string content,
            string tokenId)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(content))
                return result;

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("mid", out var midEl))
                {
                    var midStr = midEl.GetString();
                    if (decimal.TryParse(
                            midStr,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var mid) && mid > 0)
                    {
                        result[tokenId] = mid;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse single CLOB midpoint response. Content={Content}",
                    content.Length > 200 ? content[..200] : content);
            }

            return result;
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

                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Resposta simples para token único:
                    // { "mid_price": "0.45" }
                    // ou
                    // { "mid": "0.45" }
                    if (root.ValueKind == JsonValueKind.Object &&
                        (root.TryGetProperty("mid_price", out var midPriceEl) ||
                         root.TryGetProperty("mid", out midPriceEl)))
                    {
                        var midPriceStr = midPriceEl.GetString();

                        if (decimal.TryParse(
                                midStr,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var mid) && mid > 0)
                        {
                            result[tokenId!] = mid;
                        }
                    }
                }
                else
                {
                    // Resposta para múltiplos tokens:
                    // pode vir como array de objetos ou objeto indexado por token
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            ParseMidpointItem(item, result);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in root.EnumerateObject())
                        {
                            var key = prop.Name;
                            var value = prop.Value;

                            if (!tokenIds.Contains(key, StringComparer.OrdinalIgnoreCase))
                                continue;

                            // Exemplo:
                            // { "tokenA": "0.42" }
                            if (value.ValueKind == JsonValueKind.String)
                            {
                                var raw = value.GetString();

                                if (decimal.TryParse(
                                        raw,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var parsed) && parsed > 0)
                                {
                                    result[key] = parsed;
                                }

                                continue;
                            }

                            // Exemplo:
                            // { "tokenA": { "mid_price": "0.42" } }
                            // ou
                            // { "tokenA": { "mid": "0.42" } }
                            if (value.ValueKind == JsonValueKind.Object &&
                                (value.TryGetProperty("mid_price", out var nestedMidPriceEl) ||
                                 value.TryGetProperty("mid", out nestedMidPriceEl)))
                            {
                                var raw = nestedMidPriceEl.GetString();

                                if (decimal.TryParse(
                                        raw,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var parsed) && parsed > 0)
                                {
                                    result[key] = parsed;
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse batch CLOB midpoints response. Content={Content}",
                    content.Length > 200 ? content[..200] : content);
            }

            if (tokenIds.Count > 1 && result.Count == 0)
            {
                _logger.LogWarning(
                    "Polymarket CLOB returned no parsed midpoints for multi-token request. RequestedTokens={RequestedTokens} Content={Content}",
                    string.Join(",", tokenIds),
                    content.Length > 1000 ? content[..1000] : content);
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
                !item.TryGetProperty("mid", out midPriceEl) &&
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
