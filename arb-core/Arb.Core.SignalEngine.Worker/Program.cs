using Arb.Core.Infrastructure.DependencyInjection;
using Arb.Core.Infrastructure.Postgres;
using Arb.Core.SignalEngine.Worker.HostedServices;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddArbInfrastructure(builder.Configuration);

// Único serviço ativo — detecta movimento asiático e publica intents Polymarket
builder.Services.AddHostedService<PolymarketObservedSignalHostedService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await init.InitializeAsync(CancellationToken.None);
}

host.Run();