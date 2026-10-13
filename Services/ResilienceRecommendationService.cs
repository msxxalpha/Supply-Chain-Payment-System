using Indamin.Payment.Data;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Services;

public sealed class ResilienceRecommendationScore
{
    public int SupplierId { get; init; }
    public decimal? SupplyImportance { get; init; }
    public decimal? ResilienceScore { get; init; }
}

public sealed record ResilienceScoreObservation(
    int SupplierId, int SupplierPartId, int PaymentParameterId, decimal Score, decimal MaxScore);

/// <summary>Optional decision support for non-calculated payment; never writes to payment data.</summary>
public sealed class ResilienceRecommendationService(AppDbContext db, SupplierPriorityService priority)
{
    public static decimal? CombineScores(decimal financialPressure, decimal? supplyImportance) =>
        supplyImportance.HasValue
            ? Math.Round(Math.Clamp(financialPressure, 0m, 100m) * .65m +
                         Math.Clamp(supplyImportance.Value, 0m, 100m) * .35m, 1)
            : null;

    public static decimal? NormalizeScore(decimal score, decimal maxScore)
    {
        if (maxScore <= 0m || score <= 0m || score > maxScore) return null;
        if (maxScore <= 1m) return 100m;
        return Math.Clamp((score - 1m) / (maxScore - 1m) * 100m, 0m, 100m);
    }

    /// <summary>
    /// Average assessors for each part/parameter, then mappings for each supplier/parameter,
    /// then parameters equally. This prevents suppliers with many mapped parts or parameters
    /// with many mappings from dominating the importance score.
    /// </summary>
    public static Dictionary<int, decimal> AggregateSupplyImportance(IEnumerable<ResilienceScoreObservation> source)
    {
        var valid = source
            .Select(x => new { x.SupplierId, x.SupplierPartId, x.PaymentParameterId, Normalized = NormalizeScore(x.Score, x.MaxScore) })
            .Where(x => x.Normalized.HasValue)
            .ToList();

        var perPartParameter = valid
            .GroupBy(x => new { x.SupplierId, x.SupplierPartId, x.PaymentParameterId })
            .Select(g => new { g.Key.SupplierId, g.Key.SupplierPartId, g.Key.PaymentParameterId, Score = g.Average(x => x.Normalized!.Value) })
            .ToList();

        var perSupplierParameter = perPartParameter
            .GroupBy(x => new { x.SupplierId, x.PaymentParameterId })
            .Select(g => new { g.Key.SupplierId, g.Key.PaymentParameterId, Score = g.Average(x => x.Score) })
            .ToList();

        return perSupplierParameter
            .GroupBy(x => x.SupplierId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(x => x.Score), 1));
    }

    public async Task<List<ResilienceRecommendationScore>> GetScoresAsync()
    {
        var suppliers = await priority.GetAsync();
        var parameters = await db.PaymentParameters.AsNoTracking()
            .Where(p => p.IsActive && p.ParticipatesInNonCalculatedRecommendation)
            .Select(p => new { p.Id, p.MaxScore }).ToListAsync();
        if (parameters.Count == 0)
            return suppliers.Select(s => new ResilienceRecommendationScore { SupplierId = s.SupplierId }).ToList();

        var ids = parameters.Select(p => p.Id).ToArray();
        var maxScores = parameters.ToDictionary(p => p.Id, p => p.MaxScore);
        var evaluations = await db.SupplierPartAssessorEvaluations.AsNoTracking()
            .Where(e => !e.IsDefault && ids.Contains(e.PaymentParameterId) &&
                e.SupplierPart != null && e.SupplierPart.IsActive &&
                e.SupplierPart.Supplier != null && e.SupplierPart.Supplier.IsActive &&
                !e.SupplierPart.Supplier.IsNonParticipating &&
                e.SupplierPart.Part != null && e.SupplierPart.Part.IsActive)
            .Select(e => new { e.SupplierPartId, e.SupplierPart!.SupplierId, e.PaymentParameterId, e.Score })
            .ToListAsync();

        var observations = evaluations
            .Where(e => maxScores.ContainsKey(e.PaymentParameterId))
            .Select(e => new ResilienceScoreObservation(
                e.SupplierId, e.SupplierPartId, e.PaymentParameterId, e.Score, maxScores[e.PaymentParameterId]));
        var importance = AggregateSupplyImportance(observations);
        var financial = suppliers.ToDictionary(s => s.SupplierId, s => Math.Clamp(s.Priority, 0m, 100m));

        return suppliers.Select(s =>
        {
            decimal? supply = importance.TryGetValue(s.SupplierId, out var value) ? value : null;
            return new ResilienceRecommendationScore
            {
                SupplierId = s.SupplierId,
                SupplyImportance = supply,
                ResilienceScore = CombineScores(financial[s.SupplierId], supply)
            };
        }).ToList();
    }
}
