namespace Arb.Core.Contracts.Events
{
    public sealed record OddsTickV1(
    string SchemaVersion,
    string EventId,
    string CorrelationId,
    DateTime Ts,
    string Source,
    string Sport,
    string SportKey,
    string League,
    string EventKey,
    string HomeTeam,
    string AwayTeam,
    DateTime CommenceTime,
    string MarketType,
    string SelectionKey,
    double OddsDecimal
 );
}
