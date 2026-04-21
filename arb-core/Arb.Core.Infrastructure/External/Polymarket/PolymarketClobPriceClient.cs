using Arb.Core.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
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
        private readonly IServiceScopeFactory _scopeFactory;

        public PolymarketClobPriceClient(
            HttpClient httpClient,
            IOptions<PolymarketClobOptions> options,
            ILogger<PolymarketClobPriceClient> logger,
            IServiceScopeFactory scopeFactory)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
            _scopeFactory = scopeFactory;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = new Uri(_options.BaseUrl);
                _httpClient.Timeout = TimeSpan.FromMilliseconds(_options.RequestTimeoutMs);
            }
        }

        /// <summary>
        /// Consulta o midpoint de um único token.
        /// Retorna null se o token não tiver orderbook, for inválido,
        /// ou em caso de erro persistente.
        /// </summary>
        public async Task<decimal?> GetMidpointAsync(
            string tokenId,
            CancellationToken ct)
        {
            var normalizedTokenId = tokenId?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedTokenId))
            {
                _logger.LogWarning(
                    "Polymarket CLOB midpoint requested with empty tokenId.");
                return null;
            }

            var results = await GetMidpointsAsync(new[] { normalizedTokenId }, ct);

            return results.TryGetValue(normalizedTokenId, out var mid)
                ? mid
                : null;
        }

        /// <summary>
        /// Consulta o midpoint de múltiplos tokens.
        /// Retorna dicionário tokenId -> midPrice.
        /// Tokens sem orderbook ou com erro são omitidos do resultado.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, decimal>> GetMidpointsAsync(
            IReadOnlyList<string> tokenIds,
            CancellationToken ct)
        {
            var normalizedTokenIds = tokenIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedTokenIds.Length == 0)
            {
                _logger.LogWarning(
                    "Polymarket CLOB midpoint requested with no valid tokenIds.");
                return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            }

            var query = BuildMidpointQuery(normalizedTokenIds);

            for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(query, ct);

                    if (response.StatusCode == HttpStatusCode.BadRequest ||
                     response.StatusCode == HttpStatusCode.NotFound)
                    {
                        var body = await SafeReadBodyAsync(response, ct);

                        _logger.LogWarning(
                            "Polymarket CLOB midpoint returned {StatusCode} for token request. TokenCount={TokenCount} IsSingleToken={IsSingleToken} TokenIds={TokenIds} ResponseBody={ResponseBody}. No retry.",
                            (int)response.StatusCode,
                            normalizedTokenIds.Length,
                            normalizedTokenIds.Length == 1,
                            string.Join(",", normalizedTokenIds),
                            body);

                        if (response.StatusCode == HttpStatusCode.NotFound &&
                            body.Contains("No orderbook exists", StringComparison.OrdinalIgnoreCase))
                        {
                            await MarkNoOrderbookAsync(normalizedTokenIds, response.StatusCode, body, ct);
                        }

                        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    }

                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(ct);
                    var parsed = ParseMidpointResponse(content, normalizedTokenIds);

                    if (parsed.Count == 0)
                    {
                        _logger.LogDebug(
                            "Polymarket CLOB midpoint returned success but no parsed values. TokenCount={TokenCount} IsSingleToken={IsSingleToken} TokenIds={TokenIds} ResponseBody={ResponseBody}",
                            normalizedTokenIds.Length,
                            normalizedTokenIds.Length == 1,
                            string.Join(",", normalizedTokenIds),
                            content.Length > 1000 ? content[..1000] : content);
                    }

                    return parsed;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < _options.RetryCount)
                {
                    var delay = TimeSpan.FromMilliseconds(
                        _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

                    _logger.LogWarning(
                        ex,
                        "Polymarket CLOB midpoint attempt {Attempt}/{MaxAttempts} failed. Retrying in {DelayMs}ms. TokenCount={TokenCount} IsSingleToken={IsSingleToken} TokenIds={TokenIds}",
                        attempt,
                        _options.RetryCount,
                        delay.TotalMilliseconds,
                        normalizedTokenIds.Length,
                        normalizedTokenIds.Length == 1,
                        string.Join(",", normalizedTokenIds));

                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Polymarket CLOB midpoint failed after {MaxAttempts} attempt(s). TokenCount={TokenCount} IsSingleToken={IsSingleToken} TokenIds={TokenIds}",
                        _options.RetryCount,
                        normalizedTokenIds.Length,
                        normalizedTokenIds.Length == 1,
                        string.Join(",", normalizedTokenIds));

                    return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                }
            }

            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task MarkNoOrderbookAsync(
        IReadOnlyList<string> tokenIds,
        HttpStatusCode statusCode,
        string body,
        CancellationToken ct)
        {
            var utcNow = DateTime.UtcNow;
            var retryAfter = utcNow.AddHours(6);

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITokenHealthRepository>();

            foreach (var tokenId in tokenIds)
            {
                await repo.UpsertNoOrderbookAsync(
                    tokenId: tokenId,
                    httpStatus: (int)statusCode,
                    responseBody: body,
                    utcNow: utcNow,
                    retryAfter: retryAfter,
                    ct: ct);
            }
        }

        private static string BuildMidpointQuery(IReadOnlyList<string> tokenIds)
        {
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
                    if (root.ValueKind == JsonValueKind.Object &&
                        (root.TryGetProperty("mid_price", out var midPriceEl) ||
                         root.TryGetProperty("mid", out midPriceEl)))
                    {
                        var midPriceStr = midPriceEl.GetString();

                        if (decimal.TryParse(
                                midPriceStr,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var midPrice) &&
                            midPrice > 0)
                        {
                            result[tokenIds[0]] = midPrice;
                        }
                    }
                }
                else
                {
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

                            if (value.ValueKind == JsonValueKind.String)
                            {
                                var raw = value.GetString();

                                if (decimal.TryParse(
                                        raw,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var parsed) &&
                                    parsed > 0)
                                {
                                    result[key] = parsed;
                                }

                                continue;
                            }

                            if (value.ValueKind == JsonValueKind.Object &&
                                (value.TryGetProperty("mid_price", out var nestedMidPriceEl) ||
                                 value.TryGetProperty("mid", out nestedMidPriceEl)))
                            {
                                var raw = nestedMidPriceEl.GetString();

                                if (decimal.TryParse(
                                        raw,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var parsed) &&
                                    parsed > 0)
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
                _logger.LogWarning(
                    ex,
                    "Failed to parse Polymarket CLOB midpoint response. TokenIds={TokenIds} Content={Content}",
                    string.Join(",", tokenIds),
                    content.Length > 500 ? content[..500] : content);
            }

            if (tokenIds.Count > 1 && result.Count == 0)
            {
                _logger.LogDebug(
                    "Polymarket CLOB returned no parsed midpoints for multi-token request. TokenIds={TokenIds} Content={Content}",
                    string.Join(",", tokenIds),
                    content.Length > 1000 ? content[..1000] : content);
            }

            if (tokenIds.Count == 1 && result.Count == 0)
            {
                _logger.LogDebug(
                    "Polymarket CLOB returned no parsed midpoint for single-token request. TokenId={TokenId} Content={Content}",
                    tokenIds[0],
                    content.Length > 500 ? content[..500] : content);
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
                    out var midPrice) &&
                midPrice > 0)
            {
                result[tokenId] = midPrice;
            }
        }

        private static async Task<string> SafeReadBodyAsync(
            HttpResponseMessage response,
            CancellationToken ct)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(body))
                    return "<empty>";

                return body.Length > 1000 ? body[..1000] : body;
            }
            catch
            {
                return "<unavailable>";
            }
        }
    }
}