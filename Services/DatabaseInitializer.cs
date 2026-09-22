using Indamin.Payment.Data;using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Services;
public static class DatabaseInitializer{
 const string Sql="""SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('AppUsers','LookupValues','Parts','Suppliers','SupplierActivities','SupplierParts','PaymentParameters','SupplierPartEvaluations','PaymentRuns','PaymentRunInvoices','PaymentInvoiceScores','AuditLogs')""";
 public static async Task InitializeAsync(AppDbContext db){var c=await db.Database.SqlQueryRaw<int>(Sql).SingleAsync();if(c!=12)throw new InvalidOperationException("ساختار پایگاه داده کامل نیست. ابتدا Database/001_initial.sql را اجرا کنید.");}
 public static async Task SeedAsync(AppDbContext db){
  if(!await db.LookupValues.AnyAsync()){db.LookupValues.AddRange(
  new LookupValue{GroupCode="PART_TYPE",Code="RAW",Title="مواد اولیه",SortOrder=1},new LookupValue{GroupCode="PART_TYPE",Code="SEMIFINISHED",Title="نیمه‌ساخته",SortOrder=2},new LookupValue{GroupCode="PART_TYPE",Code="FINISHED",Title="کالای نهایی",SortOrder=3},new LookupValue{GroupCode="PART_TYPE",Code="PACKAGING",Title="بسته‌بندی",SortOrder=4},
  new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="METAL",Title="فلزی",SortOrder=1},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="POLYMER",Title="پلیمری",SortOrder=2},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="ELECTRICAL",Title="برقی/الکترونیکی",SortOrder=3},new LookupValue{GroupCode="SUPPLIER_ACTIVITY",Code="SERVICE",Title="خدماتی",SortOrder=4});await db.SaveChangesAsync();}
  if(!await db.PaymentParameters.AnyAsync()){db.PaymentParameters.AddRange(
  new PaymentParameter{Code="DEBT_AGE",Title="سن بدهی موثر",Type=PaymentParameterType.Time,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.DebtAgeEffective,SortOrder=1,ScoringGuide="کمتر از 25%: 1 | 25% تا 50%: 2 | 50% تا 75%: 1 | 75% تا مهلت قراردادی و تا 30 روز پس از آن: 4 | بیش از 30 روز پس از مهلت: 5"},
  new PaymentParameter{Code="DEBT_AMOUNT",Title="مبلغ بدهی",Type=PaymentParameterType.Financial,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.DebtAmount,SortOrder=2,ScoringGuide="کمتر از 3%: 1 | بیش از 3% تا 10%: 2 | بیش از 10% تا 20%: 3 | بیش از 20% تا 35%: 4 | بیش از 35%: 5"},
  new PaymentParameter{Code="PART_PRIORITY",Title="اهمیت قطعه",Type=PaymentParameterType.Part,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.Manual,SortOrder=3,ScoringGuide="1=بسیار کم، 2=کم، 3=متوسط، 4=زیاد، 5=بسیار حیاتی"},
  new PaymentParameter{Code="SUPPLIER_RELIABILITY",Title="قابلیت اتکای تامین‌کننده",Type=PaymentParameterType.Supplier,Weight=25,MaxScore=5,ScoringMethod=ParameterScoringMethod.Manual,SortOrder=4,ScoringGuide="1=ضعیف، 2=کم، 3=متوسط، 4=خوب، 5=عالی"});await db.SaveChangesAsync();}
 }}
