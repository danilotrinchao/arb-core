namespace Arb.Core.Contracts.Events
{
    public sealed record OrderIntentV1(
    string SchemaVersion,
    string IntentId,
    string CorrelationId,
    DateTime Ts,
    string Strategy,
    string Venue,
    string SportKey,
    string EventKey,
    string HomeTeam,
    string AwayTeam,
    DateTime CommenceTime,
    string MarketType,
    string SelectionKey,
    double PriceLimit,
    double Stake,
    string Side          // "YES" / "NO" ou "BUY" dependendo do seu modelo
    );
}
