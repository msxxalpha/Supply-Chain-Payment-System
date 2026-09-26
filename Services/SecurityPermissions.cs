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
 public const string SetupParameters="Permission:Setup.Parameters";
 public const string SetupParts="Permission:Setup.Parts";
 public const string SetupSuppliers="Permission:Setup.Suppliers";
 public const string SetupMappings="Permission:Setup.Mappings";
 public const string SetupEvaluations="Permission:Setup.Evaluations";
 public const string SetupLookups="Permission:Setup.Lookups";
 public const string UsersManage="Permission:Users.Manage";
 public static readonly string[] AllCodes=[DashboardView[11..],PaymentCalculate[11..],PaymentHistory[11..],PaymentApprove[11..],PaymentOrderCreate[11..],PaymentOrderDownload[11..],PaymentDelete[11..],ReportsView[11..],SetupParameters[11..],SetupParts[11..],SetupSuppliers[11..],SetupMappings[11..],SetupEvaluations[11..],SetupLookups[11..],UsersManage[11..]];
 public record Definition(string Code,string Title,string GroupTitle,int SortOrder);
 public static readonly IReadOnlyList<Definition> Definitions=[
  new("Dashboard.View","مشاهده داشبورد","عمومی",1),
  new("Payment.Calculate","محاسبه و تخصیص پرداخت","پرداخت",10),
  new("Payment.History","مشاهده سوابق تخصیص","پرداخت",11),
  new("Payment.Approve","تایید نهایی تخصیص","پرداخت",12),
  new("Payment.OrderCreate","تبدیل محاسبه به دستور پرداخت","دستور پرداخت",13),
  new("Payment.OrderDownload","دریافت فایل دستور پرداخت","دستور پرداخت",14),
  new("Payment.Delete","حذف منطقی محاسبه","پرداخت",15),
  new("Reports.View","داشبورد و گزارش‌های مالی","گزارش‌ها",20),
  new("Setup.Parameters","مدیریت پارامترهای پرداخت","اطلاعات پایه",30),
  new("Setup.Parts","مدیریت کالاها","اطلاعات پایه",31),
  new("Setup.Suppliers","مدیریت تامین‌کنندگان","اطلاعات پایه",32),
  new("Setup.Mappings","مدیریت ارتباط کالا-تامین‌کننده","اطلاعات پایه",33),
  new("Setup.Evaluations","مدیریت ارزیابی‌ها","اطلاعات پایه",34),
  new("Setup.Lookups","مدیریت سایر اطلاعات پایه","اطلاعات پایه",35),
  new("Users.Manage","مدیریت کاربران، نقش‌ها و دسترسی‌ها","امنیت",40)
 ];
 public static readonly IReadOnlyDictionary<string,string[]> DefaultRolePermissions=new Dictionary<string,string[]>{
  ["SYS_ADMIN"]=AllCodes,
  ["FINANCE_OPERATOR"]=["Dashboard.View","Payment.Calculate","Payment.History","Payment.Approve","Payment.OrderCreate","Payment.OrderDownload","Reports.View"],
  ["FINANCE_VIEWER"]=["Dashboard.View","Payment.History","Payment.OrderDownload","Reports.View"],
  ["MASTER_DATA"]=["Dashboard.View","Setup.Parameters","Setup.Parts","Setup.Suppliers","Setup.Mappings","Setup.Evaluations","Setup.Lookups"]
 };
}
