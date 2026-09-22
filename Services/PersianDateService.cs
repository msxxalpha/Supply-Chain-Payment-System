using System.Globalization;
namespace Indamin.Payment.Services;
public static class PersianDateService{
 static readonly PersianCalendar C=new();
 public static DateTime Parse(string v){var n=ToLatinDigits(v?.Trim()??"").Replace('-','/');var p=n.Split('/',StringSplitOptions.RemoveEmptyEntries);if(p.Length!=3||!int.TryParse(p[0],out var y)||!int.TryParse(p[1],out var m)||!int.TryParse(p[2],out var d))throw new ArgumentException("تاریخ شمسی نامعتبر است.");try{return DateTime.SpecifyKind(C.ToDateTime(y,m,d,0,0,0,0),DateTimeKind.Local);}catch{throw new ArgumentException("تاریخ شمسی خارج از محدوده معتبر است.");}}
 public static string ToJalali(DateTime d)=>$"{C.GetYear(d):0000}/{C.GetMonth(d):00}/{C.GetDayOfMonth(d):00}";
 public static string ToLatinDigits(string s)=>(s??"").Replace('۰','0').Replace('۱','1').Replace('۲','2').Replace('۳','3').Replace('۴','4').Replace('۵','5').Replace('۶','6').Replace('۷','7').Replace('۸','8').Replace('۹','9');
}