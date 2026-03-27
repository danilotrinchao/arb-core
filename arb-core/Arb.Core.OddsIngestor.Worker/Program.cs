using Arb.Core.Infrastructure.DependencyInjection;
using Arb.Core.OddsIngestor.Worker;
using Arb.Core.OddsIngestor.Worker.HostedServices;
using Arb.Core.OddsIngestor.Worker.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddArbInfrastructure(builder.Configuration);
builder.Services.AddHostedService<FootballCatalogSyncHostedService>();

builder.Services.Configure<OddsIngestorOptions>(
    builder.Configuration.GetSection(OddsIngestorOptions.SectionName));
var host = builder.Build();
host.Run();
