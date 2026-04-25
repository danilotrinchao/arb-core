using System.Net.Http.Json;
using System.Text.Json;
using Arb.Core.Application.Abstractions.Execution;

namespace Arb.Core.Infrastructure.External.Execution
{
    public class HttpExecutionGateway : IExecutionGateway
    {
        private readonly HttpClient _httpClient;

        public HttpExecutionGateway(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ExecutionCommandResponseDto> PlaceBuyAsync(ExecutionCommandDto command, CancellationToken ct)
        {
            var response = await _httpClient.PostAsJsonAsync("/orders/buy", command, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ExecutionCommandResponseDto
                {
                    Success = false,
                    RequestId = command.RequestId,
                    Status = "FAILED",
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = body,
                    RawJson = body
                };
            }

            return JsonSerializer.Deserialize<ExecutionCommandResponseDto>(body)!;
        }

        public async Task<ExecutionCommandResponseDto> PlaceSellAsync(ExecutionCommandDto command, CancellationToken ct)
        {
            var response = await _httpClient.PostAsJsonAsync("/orders/sell", command, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ExecutionCommandResponseDto
                {
                    Success = false,
                    RequestId = command.RequestId,
                    Status = "FAILED",
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = body,
                    RawJson = body
                };
            }

            return JsonSerializer.Deserialize<ExecutionCommandResponseDto>(body)!;
        }

        public async Task<ExecutionCommandResponseDto> CancelAsync(CancelExecutionCommandDto command, CancellationToken ct)
        {
            var response = await _httpClient.PostAsJsonAsync("/orders/cancel", command, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ExecutionCommandResponseDto
                {
                    Success = false,
                    RequestId = command.RequestId,
                    Status = "FAILED",
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    ErrorMessage = body,
                    RawJson = body
                };
            }

            return JsonSerializer.Deserialize<ExecutionCommandResponseDto>(body)!;
        }

        public async Task<OrderStatusDto> GetOrderAsync(string externalOrderId, CancellationToken ct)
        {
            return await _httpClient.GetFromJsonAsync<OrderStatusDto>($"/orders/{externalOrderId}", ct)
                   ?? new OrderStatusDto { Success = false, ExternalOrderId = externalOrderId, Status = "FAILED" };
        }

        public async Task<OrderFillsDto> GetFillsAsync(string externalOrderId, CancellationToken ct)
        {
            return await _httpClient.GetFromJsonAsync<OrderFillsDto>($"/orders/{externalOrderId}/fills", ct)
                   ?? new OrderFillsDto { Success = false, ExternalOrderId = externalOrderId };
        }

        public async Task<WalletBalanceDto> GetBalanceAsync(CancellationToken ct)
        {
            return await _httpClient.GetFromJsonAsync<WalletBalanceDto>("/wallet/balance", ct)
                   ?? new WalletBalanceDto { Success = false };
        }
    }
}