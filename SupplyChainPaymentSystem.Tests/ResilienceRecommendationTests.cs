using Indamin.Payment.Services;
using Xunit;

namespace SupplyChainPaymentSystem.Tests;

public class ResilienceRecommendationTests
{
    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(80, 40, 66)]
    [InlineData(0, 100, 35)]
    public void CombineScores_UsesProvisionalWeightedFormula(decimal financial, decimal supply, decimal expected)
        => Assert.Equal(expected, ResilienceRecommendationService.CombineScores(financial, supply));

    [Fact]
    public void CombineScores_ReturnsMissing_WhenSupplyImportanceIsUnavailable()
        => Assert.Null(ResilienceRecommendationService.CombineScores(75m, null));

    [Fact]
    public void CombineScores_ClampsInputsToScale()
        => Assert.Equal(100m, ResilienceRecommendationService.CombineScores(120m, 150m));

    [Theory]
    [InlineData(1, 5, 0)]
    [InlineData(3, 5, 50)]
    [InlineData(5, 5, 100)]
    [InlineData(1, 1, 100)]
    public void NormalizeScore_MapsValidScoresToZeroToHundred(decimal score, decimal max, decimal expected)
        => Assert.Equal(expected, ResilienceRecommendationService.NormalizeScore(score, max));

    [Fact]
    public void NormalizeScore_RejectsInvalidScores()
    {
        Assert.Null(ResilienceRecommendationService.NormalizeScore(0m, 5m));
        Assert.Null(ResilienceRecommendationService.NormalizeScore(6m, 5m));
        Assert.Null(ResilienceRecommendationService.NormalizeScore(1m, 0m));
    }

    [Fact]
    public void AggregateSupplyImportance_WeightsParametersEqually_NotByMappingCount()
    {
        var scores = new[]
        {
            new ResilienceScoreObservation(1, 10, 100, 1m, 5m),
            new ResilienceScoreObservation(1, 10, 100, 5m, 5m),
            new ResilienceScoreObservation(1, 11, 100, 5m, 5m),
            new ResilienceScoreObservation(1, 10, 200, 1m, 5m)
        };

        // Parameter 100: assessor average per mapping = 50, then mapping average = 75.
        // Parameter 200 = 0. Equal parameter weighting yields 37.5, not a mapping-count-weighted result.
        Assert.Equal(37.5m, ResilienceRecommendationService.AggregateSupplyImportance(scores)[1]);
    }

    [Fact]
    public void AggregateSupplyImportance_IgnoresInvalidScores_AndDoesNotCreateMissingSuppliers()
    {
        var scores = new[]
        {
            new ResilienceScoreObservation(1, 10, 100, 0m, 5m),
            new ResilienceScoreObservation(1, 10, 100, 6m, 5m)
        };

        Assert.Empty(ResilienceRecommendationService.AggregateSupplyImportance(scores));
    }
}
