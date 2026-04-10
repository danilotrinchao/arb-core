using System;

namespace Arb.Core.Application.Request
{
    public sealed class ObservedSoccerSelectionSnapshot
    {
        public string EventId { get; init; } = string.Empty;

        public string SportKey { get; init; } = string.Empty;

        public string? BookmakerKey { get; init; }

        public string? CommenceTime { get; init; }

        public string ObservedAt { get; init; } = string.Empty;

        // Futebol: HomeTeam / AwayTeam
        // NBA: pode ser vazio (selectionKey=SIDE_A/SIDE_B contém o time)
        public string HomeTeam { get; init; } = string.Empty;

        public string AwayTeam { get; init; } = string.Empty;

        // Futebol: HOME, AWAY
        // NBA: SIDE_A, SIDE_B
        // Também pode ser DRAW, TOTAL_OVER, etc. — o projector ignora
        public string SelectionKey { get; init; } = string.Empty;

        public decimal Price { get; init; }

        // Novo: time observado direto (NBA H2H via SIDE_A/SIDE_B labels)
        // Futebol: deixa vazio. NBA: contém o time do lado observado
        public string? DirectObservedTeam { get; init; }
    }
}
