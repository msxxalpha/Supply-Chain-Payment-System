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
 {
  Assert.Equal(expected, ResilienceRecommendationService.CombineScores(financial, supply));
 }
 [Fact]
 public void CombineScores_ReturnsMissing_WhenSupplyImportanceIsUnavailable()
 {
  Assert.Null(ResilienceRecommendationService.CombineScores(75m, null));
 }
 [Fact]
 public void CombineScores_ClampsInputsToScale()
 {
  Assert.Equal(100m, ResilienceRecommendationService.CombineScores(120m, 150m));
 }
}
