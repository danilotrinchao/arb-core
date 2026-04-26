using Arb.Core.Executor.Worker;
using Arb.Core.Executor.Worker.HostedServices;
using Arb.Core.Executor.Worker.Options;
using Arb.Core.Infrastructure.DependencyInjection;
using Arb.Core.Infrastructure.External.Polymarket;
using Arb.Core.Infrastructure.Postgres;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArbInfrastructure(builder.Configuration);

builder.Services.Configure<ExecutorOptions>(
    builder.Configuration.GetSection(ExecutorOptions.SectionName));

builder.Services.Configure<RiskOptions>(
    builder.Configuration.GetSection(RiskOptions.SectionName));

builder.Services.Configure<SettlementOptions>(
    builder.Configuration.GetSection(SettlementOptions.SectionName));

builder.Services.Configure<EntryQualityOptions>(
    builder.Configuration.GetSection(EntryQualityOptions.SectionName));

builder.Services.AddHostedService<Worker>();

//builder.Services.AddHostedService<PolymarketSportsWebSocketSettlementService>();

builder.Services.Configure<PolymarketClobOptions>(
    builder.Configuration.GetSection(PolymarketClobOptions.SectionName));

builder.Services.AddHttpClient<PolymarketClobPriceClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<PolymarketClobOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
    client.DefaultRequestHeaders.Add("User-Agent", "ArbBot/1.0");
});

builder.Services.AddHostedService<PolymarketExitMonitorService>();

var executionMode = builder.Configuration["Executor:ExecutionMode"];
if (string.Equals(executionMode, "Real", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<ExecutionReconciliationWorker>();
    Console.WriteLine("ExecutionReconciliationWorker enabled because ExecutionMode=Real.");
}
else
{
    Console.WriteLine($"ExecutionReconciliationWorker disabled because ExecutionMode={executionMode ?? "null"}.");
}

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await dbInitializer.InitializeAsync(CancellationToken.None);
}

host.Run();