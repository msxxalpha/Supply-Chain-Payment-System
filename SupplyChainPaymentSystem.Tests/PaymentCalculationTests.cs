using Indamin.Payment.Services;using Xunit;
namespace Indamin.Payment.Tests;
public class PaymentCalculationTests{
 [Theory][InlineData(10,100,1)][InlineData(25,100,2)][InlineData(50,100,2)][InlineData(51,100,1)][InlineData(75,100,1)][InlineData(76,100,4)][InlineData(100,100,4)][InlineData(130,100,4)][InlineData(131,100,5)]public void AgeScore_FollowsBoundaries(int a,int c,int e)=>Assert.Equal(e,PaymentCalculationService.AgeScore(a,c));
 [Theory][InlineData(2.99,100,1)][InlineData(3,100,2)][InlineData(10,100,2)][InlineData(10.01,100,3)][InlineData(20,100,3)][InlineData(20.01,100,4)][InlineData(35,100,4)][InlineData(35.01,100,5)]public void DebtAmount_FollowsBoundaries(decimal d,decimal t,int e)=>Assert.Equal(e,PaymentCalculationService.DebtAmountScore(d,t));
 [Fact]public void CompositeKey_NormalizesArabicLetters()=>Assert.Equal(PaymentCalculationService.Key("R","انبار","قطعه ك","تامین‌کننده"),PaymentCalculationService.Key("r","انبار","قطعه ک","تامین‌کننده"));
}
