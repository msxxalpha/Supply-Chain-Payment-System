namespace Indamin.Payment.Services;
public static class SecurityPermissions{
 public const string Claim="Permission";
 public const string DashboardView="Permission:Dashboard.View";
 public const string PaymentCalculate="Permission:Payment.Calculate";
 public const string PaymentHistory="Permission:Payment.History";
 public const string PaymentApprove="Permission:Payment.Approve";
 public const string PaymentOrderCreate="Permission:Payment.OrderCreate";
 public const string PaymentOrderDownload="Permission:Payment.OrderDownload";
 public const string PaymentDelete="Permission:Payment.Delete";
 public const string ReportsView="Permission:Reports.View";
 public const string SupplierOverview="Permission:Reports.SupplierOverview";
 public const string AssessorPerformance="Permission:Reports.AssessorPerformance";
 public const string PriceListAnalysis="Permission:Reports.PriceListAnalysis";
 public const string SetupParameters="Permission:Setup.Parameters";
 public const string SetupSystemParameters="Permission:Setup.SystemParameters";
 public const string PaymentNonCalculated="Permission:Payment.NonCalculated";
 public const string SetupParts="Permission:Setup.Parts";
 public const string SetupSuppliers="Permission:Setup.Suppliers";
 public const string SetupMappings="Permission:Setup.Mappings";
 public const string SetupEvaluations="Permission:Setup.Evaluations";
 public const string SetupLookups="Permission:Setup.Lookups"; public const string SetupQueries="Permission:Setup.Queries"; public const string SetupSupplierPriceList="Permission:Setup.SupplierPriceList"; public const string SupplierPartEvaluationView="Permission:SupplierPartEvaluation.View";
 public const string UsersManage="Permission:Users.Manage";
 public static readonly string[] AllCodes=[DashboardView[11..],PaymentCalculate[11..],PaymentHistory[11..],PaymentApprove[11..],PaymentOrderCreate[11..],PaymentOrderDownload[11..],PaymentDelete[11..],PaymentNonCalculated[11..],ReportsView[11..],SupplierOverview[11..],AssessorPerformance[11..],PriceListAnalysis[11..],SetupParameters[11..],SetupSystemParameters[11..],SetupParts[11..],SetupSuppliers[11..],SetupMappings[11..],SetupEvaluations[11..],SetupLookups[11..],SetupQueries[11..],SetupSupplierPriceList[11..],SupplierPartEvaluationView[11..],UsersManage[11..]];
 public static string SupplierPartParameterPermission(int parameterId)=>$"Permission:SupplierPartEvaluation.Parameter.{parameterId}";
 public record Definition(string Code,string Title,string GroupTitle,int SortOrder);
 public static readonly IReadOnlyList<Definition> Definitions=[
  new("Dashboard.View","مشاهده داشبورد","عمومی",1),
  new("Payment.Calculate","محاسبه و تخصیص پرداخت","پرداخت",10),
  new("Payment.History","مشاهده سوابق تخصیص","پرداخت",11),
  new("Payment.Approve","تایید نهایی تخصیص","پرداخت",12),
  new("Payment.OrderCreate","تبدیل محاسبه به دستور پرداخت","دستور پرداخت",13),
  new("Payment.OrderDownload","دریافت فایل دستور پرداخت","دستور پرداخت",14),
  new("Payment.Delete","حذف منطقی محاسبه","پرداخت",15),
  new("Payment.NonCalculated","پرداخت غیرمحاسباتی","پرداخت",16),
  new("Reports.View","داشبورد و گزارش‌های مالی","گزارش‌ها",20),
  new("Reports.SupplierOverview","مرور تامین‌کننده","گزارش‌ها",21),
  new("Reports.AssessorPerformance","گزارش عملکرد ارزیابان قطعه–تامین‌کننده","گزارش‌ها",22),
  new("Reports.PriceListAnalysis","تحلیل فهرست بها و روند قیمت","گزارش‌ها",23),
  new("Setup.Parameters","مدیریت پارامترهای پرداخت","اطلاعات پایه",30),
  new("Setup.SystemParameters","مدیریت پارامترهای سیستم","اطلاعات پایه",31),
  new("Setup.Parts","مدیریت کالاها","اطلاعات پایه",32),
  new("Setup.Suppliers","مدیریت تامین‌کنندگان","اطلاعات پایه",33),
  new("Setup.Mappings","مدیریت ارتباط کالا-تامین‌کننده","اطلاعات پایه",34),
  new("Setup.Evaluations","مدیریت ارزیابی‌ها","اطلاعات پایه",35),
  new("Setup.Lookups","مدیریت سایر اطلاعات پایه","اطلاعات پایه",36),
  new("Setup.Queries","مدیریت کوئری‌ها","اطلاعات پایه",37),
  new("Setup.SupplierPriceList","مدیریت فهرست بها تامین‌کنندگان","اطلاعات پایه",38),
  new("SupplierPartEvaluation.View","صفحه ارزیابی قطعه–تامین‌کننده","ارزیابی قطعه–تامین‌کننده",50),
  new("Users.Manage","مدیریت کاربران، نقش‌ها و دسترسی‌ها","امنیت",40)
 ];
 public static readonly IReadOnlyDictionary<string,string[]> DefaultRolePermissions=new Dictionary<string,string[]>{
  ["SYS_ADMIN"]=AllCodes,
  ["FINANCE_OPERATOR"]=["Dashboard.View","Payment.Calculate","Payment.NonCalculated","Payment.History","Payment.Approve","Payment.OrderCreate","Payment.OrderDownload","Reports.View","Reports.SupplierOverview","Reports.PriceListAnalysis"],
  ["FINANCE_VIEWER"]=["Dashboard.View","Payment.History","Payment.OrderDownload","Reports.View","Reports.SupplierOverview","Reports.PriceListAnalysis"],
  ["MASTER_DATA"]=["Dashboard.View","Setup.Parameters","Setup.SystemParameters","Setup.Parts","Setup.Suppliers","Setup.Mappings","Setup.Evaluations","Setup.Lookups","Setup.Queries","Setup.SupplierPriceList","Reports.PriceListAnalysis"],
  ["SUPPLIER_PART_ASSESSOR"]=["SupplierPartEvaluation.View"]
 };
}
