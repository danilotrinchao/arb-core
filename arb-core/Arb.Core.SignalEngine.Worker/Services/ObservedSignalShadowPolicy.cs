using Arb.Core.SignalEngine.Worker.Options;
using Microsoft.Extensions.Options;

namespace Arb.Core.SignalEngine.Worker.Services
{
    public class ObservedSignalShadowPolicy
    {
        private readonly SignalEngineOptions _options;

        public ObservedSignalShadowPolicy(IOptions<SignalEngineOptions> options)
        {
            _options = options.Value;
        }

        public ShadowDecisionResult Evaluate(
            ObservedSignalQualifier.QualificationResult qualification)
        {
            if (!_options.EnableShadowSignalPolicy)
            {
                return ShadowDecisionResult.Publish(_options.ShadowPolicyVersion);
            }

            // Esses checks só devem atuar quando o dado realmente existir.
            // No SignalEngine, InitialEdge e DeltaVsComparableTarget ficam null
            // porque o preço real da Polymarket só é buscado no Executor.
            if (qualification.DeltaVsComparableTarget.HasValue &&
                qualification.DeltaVsComparableTarget.Value >= (decimal)_options.ShadowMaxPositiveDeltaGlobal)
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_DELTA_VS_COMPARABLE_TARGET_TOO_HIGH",
                    _options.ShadowPolicyVersion);
            }

            if (qualification.IsLongHorizon &&
                qualification.DeltaVsComparableTarget.HasValue &&
                qualification.DeltaVsComparableTarget.Value >= (decimal)_options.ShadowMaxPositiveDeltaLongHorizon)
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_LONG_HORIZON_DELTA_TOO_HIGH",
                    _options.ShadowPolicyVersion);
            }

            if (qualification.InitialEdge.HasValue &&
                qualification.InitialEdge.Value < (decimal)_options.ShadowMinInitialEdgeGlobal)
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_INITIAL_EDGE_BELOW_GLOBAL_MINIMUM",
                    _options.ShadowPolicyVersion);
            }

            if (qualification.IsLongHorizon &&
                qualification.InitialEdge.HasValue &&
                qualification.InitialEdge.Value < (decimal)_options.ShadowMinInitialEdgeLongHorizon)
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_INITIAL_EDGE_BELOW_LONG_HORIZON_MINIMUM",
                    _options.ShadowPolicyVersion);
            }

            if (string.Equals(qualification.SignalRiskCategory, "HIGH", StringComparison.OrdinalIgnoreCase))
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_SIGNAL_RISK_HIGH",
                    _options.ShadowPolicyVersion);
            }

            if (qualification.SignalQualityScore < _options.ShadowMinSignalQualityScore)
            {
                return ShadowDecisionResult.Reject(
                    "SHADOW_SIGNAL_QUALITY_SCORE_BELOW_MINIMUM",
                    _options.ShadowPolicyVersion);
            }

            return ShadowDecisionResult.Publish(_options.ShadowPolicyVersion);
        }

        public record ShadowDecisionResult(
            string Decision,
            string? RejectReason,
            string PolicyVersion)
        {
            public static ShadowDecisionResult Publish(string policyVersion)
                => new("WOULD_PUBLISH", null, policyVersion);

            public static ShadowDecisionResult Reject(string reason, string policyVersion)
                => new("WOULD_REJECT", reason, policyVersion);
        }
    }
}