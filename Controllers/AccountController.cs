using Indamin.Payment.Services;
using System.Security.Claims;using Indamin.Payment.Data;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Authentication;using Microsoft.AspNetCore.Authentication.Cookies;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace Indamin.Payment.Controllers;
public class AccountController(AppDbContext db,CompanySettingsService companySettings):Controller{
 string DefaultLandingUrl(ClaimsPrincipal? principal = null)
 {
  principal ??= User;
  if(principal.IsInRole("SUPPLIER_PART_ASSESSOR")) return "/SupplierPartEvaluation/Dashboard";
  if(principal.HasClaim("Permission","Dashboard.View")) return "/";
  if(principal.HasClaim("Permission","SupplierPartEvaluation.View")) return "/SupplierPartEvaluation";
  if(principal.HasClaim("Permission","Reports.View")) return "/Reports";
  if(principal.HasClaim("Permission","Payment.Calculate")) return "/PaymentWizard";
  if(principal.HasClaim("Permission","Setup.Parts")) return "/Setup/Parts";
  if(principal.HasClaim("Permission","Setup.SupplierPriceList")) return "/SupplierPriceList";
  return "/Account/Denied";
 }

 // Authentication middleware can send "/" as returnUrl when an unauthorised user
 // initially requests the dashboard. Do not send that user straight back to the
 // protected dashboard after login; use their first permitted landing page instead.
 string LoginRedirectUrl(string? returnUrl, ClaimsPrincipal? principal = null)
 {
  principal ??= User;
  if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
      return DefaultLandingUrl(principal);

  var path = returnUrl.Split('?', '#')[0].TrimEnd('/');
  var isDashboardPath = path.Length == 0 ||
      path.Equals("/Home", StringComparison.OrdinalIgnoreCase);

  if (isDashboardPath && principal.IsInRole("SUPPLIER_PART_ASSESSOR"))
      return "/SupplierPartEvaluation/Dashboard";

  if (isDashboardPath && !principal.HasClaim("Permission", "Dashboard.View"))
      return DefaultLandingUrl(principal);

  return returnUrl;
 }

 [AllowAnonymous][HttpGet] public async Task<IActionResult> Login(string? returnUrl=null)=>User.Identity?.IsAuthenticated==true?Redirect(LoginRedirectUrl(returnUrl)):(IActionResult)View(new LoginVm{returnUrl=returnUrl,Company=await companySettings.GetAsync()});
 [AllowAnonymous][HttpPost][ValidateAntiForgeryToken] public async Task<IActionResult> Login(LoginVm m){if(string.IsNullOrWhiteSpace(m.UserName)||string.IsNullOrWhiteSpace(m.Password)){ModelState.AddModelError("","نام کاربری و رمز عبور الزامی است.");m.Company=await companySettings.GetAsync();return View(m);}var u=await db.Users.SingleOrDefaultAsync(x=>x.UserName==m.UserName.Trim()&&x.IsActive);if(u==null||!PasswordHasher.Verify(m.Password,u.PasswordHash)){ModelState.AddModelError("","نام کاربری یا رمز عبور نادرست است.");m.Company=await companySettings.GetAsync();return View(m);}var roleCodes=await db.UserRoles.Where(x=>x.UserId==u.Id&&x.Role!.IsActive).Select(x=>x.Role!.Code).Distinct().ToListAsync();var shouldLogEvaluatorActivity=await db.UserRoles.AnyAsync(x=>x.UserId==u.Id&&x.Role!.IsActive&&x.Role.IsEvaluatorLogEnabled);var permissionCodes=await db.UserRoles.Where(x=>x.UserId==u.Id&&x.Role!.IsActive).Join(db.RolePermissions,ur=>ur.RoleId,rp=>rp.RoleId,(ur,rp)=>rp).Where(x=>x.Permission!.Code!="").Select(x=>x.Permission!.Code).Distinct().ToListAsync();if(u.IsAdmin)permissionCodes=SecurityPermissions.AllCodes.ToList();var claimList=new List<Claim>{new(ClaimTypes.Name,u.DisplayName),new("UserId",u.Id.ToString()),new("IsAdmin",u.IsAdmin?"1":"0")};claimList.AddRange(roleCodes.Select(x=>new Claim(ClaimTypes.Role,x)));claimList.AddRange(permissionCodes.Select(x=>new Claim(SecurityPermissions.Claim,x)));if(shouldLogEvaluatorActivity)claimList.Add(new Claim("EvaluatorLogEnabled","1"));var claims=claimList.ToArray();
  var signedInPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme));
  await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,signedInPrincipal);
  if(shouldLogEvaluatorActivity)
  {
      var now=DateTime.UtcNow;
      var previous=await db.UserActivitySessions.Where(x=>x.UserId==u.Id&&x.LogoutAtUtc==null).ToListAsync();
      foreach(var session in previous)
      {
          session.LastSeenAtUtc=now;
          session.LogoutAtUtc=now;
          session.DurationSeconds=Math.Max(0,(int)Math.Min(int.MaxValue,(now-session.LoginAtUtc).TotalSeconds));
      }
      db.UserActivitySessions.Add(new UserActivitySession
      {
          UserId=u.Id,
          RoleCode="EVALUATOR_ACTIVITY",
          LoginAtUtc=now,
          LastSeenAtUtc=now,
          DurationSeconds=0
      });
      await db.SaveChangesAsync();
  }
  return Redirect(LoginRedirectUrl(m.returnUrl, signedInPrincipal));}
 [Authorize][HttpPost][ValidateAntiForgeryToken] public async Task<IActionResult> Logout(){
  var userIdValue=User.FindFirst("UserId")?.Value;
  if(int.TryParse(userIdValue,out var userId))
  {
      var now=DateTime.UtcNow;
      var active=await db.UserActivitySessions.Where(x=>x.UserId==userId&&x.LogoutAtUtc==null).ToListAsync();
      foreach(var session in active)
      {
          session.LastSeenAtUtc=now;
          session.LogoutAtUtc=now;
          session.DurationSeconds=Math.Max(0,(int)Math.Min(int.MaxValue,(now-session.LoginAtUtc).TotalSeconds));
      }
      await db.SaveChangesAsync();
  }
  await HttpContext.SignOutAsync();
  return RedirectToAction(nameof(Login));
}
 [Authorize][HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> PingEvaluatorActivity()
 {
  var userIdValue=User.FindFirst("UserId")?.Value;
  if(!int.TryParse(userIdValue,out var userId))
      return NoContent();
  if(!await db.UserRoles.AnyAsync(x=>x.UserId==userId&&x.Role!.IsActive&&x.Role.IsEvaluatorLogEnabled))
      return NoContent();

  try
  {
      var now=DateTime.UtcNow;
      var session=await db.UserActivitySessions
          .Where(x=>x.UserId==userId&&x.LogoutAtUtc==null)
          .OrderByDescending(x=>x.LoginAtUtc)
          .FirstOrDefaultAsync();
      if(session!=null)
      {
          session.LastSeenAtUtc=now;
          session.DurationSeconds=Math.Max(0,(int)Math.Min(int.MaxValue,(now-session.LoginAtUtc).TotalSeconds));
          await db.SaveChangesAsync();
      }
  }
  catch
  {
      // Activity logging must never interrupt the evaluator experience.
  }
  return NoContent();
 }

 [AllowAnonymous]public IActionResult Denied()=>Content("دسترسی به این بخش برای کاربر شما مجاز نیست.");
 public class LoginVm{public string? UserName{get;set;}public string? Password{get;set;}public string? returnUrl{get;set;}public CompanySettings Company{get;set;}=new();}
}