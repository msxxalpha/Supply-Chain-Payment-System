using System.Globalization;
using System.Text;
using Indamin.Payment.Data;
using PuppeteerSharp;
namespace Indamin.Payment.Services;
public class PaymentOrderPdfService(IConfiguration configuration,IWebHostEnvironment environment){
 static readonly CultureInfo Fa=new("fa-IR");
 public async Task<byte[]> GenerateAsync(CompanySettings company,PaymentRun run,IReadOnlyList<PaymentRunSupplierSummary> summaries){
  var browserPath=await ResolveBrowserAsync();
  await using var browser=await Puppeteer.LaunchAsync(new LaunchOptions{Headless=true,ExecutablePath=browserPath,Args=["--no-sandbox","--disable-gpu","--font-render-hinting=none"]});
  await using var page=await browser.NewPageAsync();
  await page.SetViewportAsync(new ViewPortOptions{Width=1240,Height=1754});
  await page.SetContentAsync(BuildHtml(company,run,summaries),new NavigationOptions{WaitUntil=[WaitUntilNavigation.Load],Timeout=30000});
  await page.WaitForFunctionAsync("document.fonts ? document.fonts.status === 'loaded' : true",new WaitForFunctionOptions{Timeout=15000});
  return await page.PdfDataAsync(new PdfOptions{Format=PaperFormat.A4,PrintBackground=true,PreferCSSPageSize=true,MarginOptions=new MarginOptions{Top="12mm",Bottom="14mm",Left="12mm",Right="12mm"}});
 }
 async Task<string> ResolveBrowserAsync(){
  var configured=configuration["PaymentOrderPdf:BrowserExecutablePath"]??Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
  if(!string.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return configured;
  var cache=configuration["PaymentOrderPdf:BrowserCachePath"];
  if(string.IsNullOrWhiteSpace(cache))cache=Path.Combine(environment.ContentRootPath,"App_Data","Puppeteer");
  Directory.CreateDirectory(cache);
  var fetcher=new BrowserFetcher(new BrowserFetcherOptions{Path=cache});
  var installed=await fetcher.DownloadAsync();
  return installed.GetExecutablePath();
 }
 static string H(string? value)=>System.Net.WebUtility.HtmlEncode(value??"");
 static string Money(decimal value)=>value.ToString("N0",Fa);
 static string BuildHtml(CompanySettings company,PaymentRun run,IReadOnlyList<PaymentRunSupplierSummary> summaries){
  var logo=company.LogoBytes?.Length>0?$"data:{H(company.LogoContentType)};base64,{Convert.ToBase64String(company.LogoBytes)}":"";
  var orderDate=run.PaymentOrderedAt.HasValue?PersianDateService.ToJalali(run.PaymentOrderedAt.Value.ToLocalTime()):"";
  var prepared=H(string.IsNullOrWhiteSpace(run.PreparedByNameSnapshot)? "تهیه کننده":run.PreparedByNameSnapshot);
  var confirmed=H(string.IsNullOrWhiteSpace(run.ConfirmedByNameSnapshot)? "تایید کننده":run.ConfirmedByNameSnapshot);
  var approved=H(string.IsNullOrWhiteSpace(run.PaymentOrderApproverNameSnapshot)? "تصویب کننده":run.PaymentOrderApproverNameSnapshot);
  var rows=new StringBuilder();
  var total=summaries.Sum(x=>x.AllocatedAmount);
  var n=1;
  foreach(var x in summaries.OrderByDescending(x=>x.AllocatedAmount).ThenBy(x=>x.SupplierTitle))
  { var share=total>0?(x.AllocatedAmount/total*100m).ToString("0.00",Fa):"0.00";
    rows.Append($"<tr><td>{n++}</td><td class='supplier'>{H(x.SupplierTitle)}</td><td>{x.InvoiceCount.ToString("N0",Fa)}</td><td class='money'>{Money(x.AllocatedAmount)} {H(run.AmountUnit)}</td><td>{share}٪</td></tr>"); }
  return $@"<!doctype html><html lang='fa' dir='rtl'><head><meta charset='utf-8'><style>
@font-face{{font-family:Vazir;src:url('https://cdn.jsdelivr.net/gh/rastikerdar/vazir-font@v30.1.0/dist/Vazir-Regular.woff2') format('woff2');font-weight:100 900;font-style:normal}}
*{{box-sizing:border-box}}html,body{{margin:0;padding:0}}body{{font-family:Vazir,Tahoma,Arial,sans-serif;color:#20374b;background:#fff;direction:rtl;font-size:11px}}@page{{size:A4;margin:12mm}}
.page{{min-height:270mm;display:flex;flex-direction:column}}.header{{border-radius:14px;padding:18px 20px;background:linear-gradient(135deg,#102a43,#1f5f8b);color:#fff;display:flex;justify-content:space-between;gap:18px;align-items:center}}.brand{{display:flex;align-items:center;gap:12px}}.logo{{width:62px;height:62px;border-radius:12px;background:#fff;padding:5px;object-fit:contain}}.brand h1{{font-size:17px;margin:0 0 5px;color:#fff}}.brand p{{font-size:9px;margin:0;color:#d8e7f0}}.order-badge{{min-width:150px;text-align:center;border:1px solid rgba(255,255,255,.25);background:rgba(255,255,255,.08);border-radius:12px;padding:9px}}.order-badge b{{display:block;font-size:13px}}.order-badge span{{display:block;font-size:9px;color:#d3e2eb;margin-top:4px}}
.meta{{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:12px 0 16px}}.meta-card{{border:1px solid #dfe8ee;background:#f7fafc;border-radius:10px;padding:9px 11px}}.meta-card label{{display:block;color:#8295a6;font-size:8px;margin-bottom:4px}}.meta-card strong{{display:block;color:#102a43;font-size:10px;line-height:1.7}}
.section-title{{display:flex;align-items:center;justify-content:space-between;margin:7px 0 8px}}.section-title h2{{font-size:13px;color:#102a43;margin:0}}.section-title span{{font-size:8px;color:#8b9ca9}}table{{width:100%;border-collapse:separate;border-spacing:0;overflow:hidden;border:1px solid #dfe8ee;border-radius:11px}}thead th{{background:#edf3f7;color:#4b6478;font-size:9px;padding:9px;border-bottom:1px solid #dfe8ee}}tbody td{{padding:9px;border-bottom:1px solid #edf1f4;font-size:9px;text-align:center}}tbody tr:last-child td{{border-bottom:0}}tbody tr:nth-child(even) td{{background:#fafcfd}}.supplier{{font-weight:800;text-align:right}}.money{{font-weight:900;color:#1f5f8b}}tfoot td{{padding:10px;background:#f3f7fa;font-weight:900;color:#102a43;border-top:2px solid #cddce5}}
.footer{{margin-top:auto;padding-top:18px;border-top:1px solid #dfe8ee}}.signatures{{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:12px}}.signature{{height:62px;border:1px dashed #b8c8d4;border-radius:11px;padding:9px 11px;display:flex;flex-direction:column;justify-content:space-between;text-align:center}}.signature b{{font-size:10px;color:#4e687c}}.signature span{{font-size:9px;color:#8295a6}}.footer-note{{display:flex;justify-content:space-between;color:#96a5b0;font-size:7.5px;margin-top:9px}}
</style></head><body><div class='page'>
<div class='header'><div class='brand'>{(string.IsNullOrWhiteSpace(logo)?"<div class='logo'></div>":$"<img class='logo' src='{logo}'>")}<div><h1>{H(company.CompanyName)}</h1><p>{H(company.SystemName)}</p></div></div><div class='order-badge'><b>دستور پرداخت</b><span>شماره {H(run.PaymentOrderNumber)}</span><span>تاریخ صدور {H(orderDate)}</span></div></div>
<div class='meta'>
<div class='meta-card'><label>عنوان محاسبه</label><strong>{H(run.Title)}</strong></div><div class='meta-card'><label>تاریخ محاسبه</label><strong>{H(run.CalculationDateJalali)}</strong></div><div class='meta-card'><label>دوره رسیدها</label><strong>{H(run.PeriodFromJalali)} تا {H(run.PeriodToJalali)}</strong></div><div class='meta-card'><label>مبلغ کل قابل تخصیص</label><strong>{Money(run.TotalAllocationBudget)} {H(run.AmountUnit)}</strong></div>
<div class='meta-card'><label>بدهی جذب‌شده</label><strong>{Money(run.ImportedDebt)} {H(run.AmountUnit)}</strong></div><div class='meta-card'><label>مانده بدهی در زمان محاسبه</label><strong>{Money(run.RemainingDebt)} {H(run.AmountUnit)}</strong></div><div class='meta-card'><label>تخصیص نهایی</label><strong>{Money(total)} {H(run.AmountUnit)}</strong></div><div class='meta-card'><label>نام فایل منبع</label><strong>{H(run.SourceFileName??"-")}</strong></div>
</div>
<div class='section-title'><h2>فهرست نهایی تخصیص به تفکیک تامین‌کنندگان</h2><span>{summaries.Count.ToString("N0",Fa)} تامین‌کننده</span></div>
<table><thead><tr><th>ردیف</th><th>تامین‌کننده</th><th>تعداد رسید</th><th>مبلغ تخصیص‌یافته</th><th>سهم</th></tr></thead><tbody>{rows}</tbody><tfoot><tr><td colspan='3'>جمع کل</td><td>{Money(total)} {H(run.AmountUnit)}</td><td>۱۰۰٪</td></tr></tfoot></table>
<div class='footer'><div class='signatures'><div class='signature'><b>تهیه کننده</b><span>{prepared}</span><span>امضاء / تاریخ</span></div><div class='signature'><b>تایید کننده</b><span>{confirmed}</span><span>امضاء / تاریخ</span></div><div class='signature'><b>تصویب کننده</b><span>{approved}</span><span>امضاء / تاریخ</span></div></div><div class='footer-note'><span>{H(company.FooterText??company.SystemName)}</span><span>این سند پس از صدور دستور پرداخت غیرقابل ویرایش است.</span></div></div>
</div></body></html>";
 }
}
