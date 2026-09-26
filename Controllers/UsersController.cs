using Indamin.Payment.Data;
using Indamin.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Indamin.Payment.Controllers;

[Authorize(Policy=SecurityPermissions.UsersManage)]
public class UsersController(AppDbContext db):Controller
{
 int UserId=>int.TryParse(User.FindFirst("UserId")?.Value,out var id)?id:0;
 async Task Log(string action,string entity,string id,string details){db.AuditLogs.Add(new AuditLog{Action=action,Entity=entity,EntityId=id,Details=details,UserId=UserId});await db.SaveChangesAsync();}

 [HttpGet]
 public async Task<IActionResult> Index(){
  var users=await db.Users.AsNoTracking().OrderBy(x=>x.UserName).ToListAsync();
  var userRoles=await db.UserRoles.Include(x=>x.Role).AsNoTracking().ToListAsync();
  var roles=await db.Roles.OrderBy(x=>x.IsSystem?0:1).ThenBy(x=>x.Title).ToListAsync();
  var permissions=await db.Permissions.OrderBy(x=>x.GroupTitle).ThenBy(x=>x.SortOrder).ToListAsync();
  var rolePermissions=await db.RolePermissions.AsNoTracking().GroupBy(x=>x.RoleId).ToDictionaryAsync(g=>g.Key,g=>g.Select(x=>x.PermissionId).ToList());
  var userRows=users.Select(u=>new UserRow(u.Id,u.UserName,u.DisplayName,u.IsAdmin,u.IsActive,userRoles.Where(x=>x.UserId==u.Id).Select(x=>x.RoleId).ToList(),userRoles.Where(x=>x.UserId==u.Id&&x.Role!=null).Select(x=>x.Role!.Title).OrderBy(x=>x).ToList())).ToList();
  return View(new UsersVm(userRows,roles,permissions,rolePermissions));
 }

 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> SaveUser(int? id,string userName,string displayName,string? password,bool isAdmin,bool isActive,int[]? roleIds){
  userName=(userName??"").Trim();displayName=(displayName??"").Trim();
  if(userName==""||displayName==""){TempData["Error"]="نام کاربری و نام نمایشی الزامی است.";return RedirectToAction(nameof(Index));}
  var existing=id.HasValue?await db.Users.FindAsync(id.Value):null;
  if(id.HasValue&&existing==null)return NotFound();
  if(id==UserId&&!isActive){TempData["Error"]="کاربر جاری نمی‌تواند حساب خود را غیرفعال کند.";return RedirectToAction(nameof(Index));}
  if(await db.Users.AnyAsync(x=>x.Id!=(id??0)&&x.UserName==userName)){TempData["Error"]="نام کاربری تکراری است.";return RedirectToAction(nameof(Index));}
  if(existing==null){
   if(string.IsNullOrWhiteSpace(password)){TempData["Error"]="برای کاربر جدید رمز عبور الزامی است.";return RedirectToAction(nameof(Index));}
   existing=new AppUser{UserName=userName,DisplayName=displayName,IsAdmin=isAdmin,IsActive=isActive,PasswordHash=PasswordHasher.Hash(password)};
   db.Users.Add(existing);await db.SaveChangesAsync();
  }else{
   existing.UserName=userName;existing.DisplayName=displayName;existing.IsAdmin=isAdmin;existing.IsActive=isActive;if(!string.IsNullOrWhiteSpace(password))existing.PasswordHash=PasswordHasher.Hash(password);await db.SaveChangesAsync();
  }
  var selected=(roleIds??[]).Distinct().ToHashSet();
  if(isAdmin){var adminRole=await db.Roles.SingleAsync(x=>x.Code=="SYS_ADMIN");selected.Add(adminRole.Id);}
  var validRoleIds=await db.Roles.Where(x=>x.IsActive&&selected.Contains(x.Id)).Select(x=>x.Id).ToListAsync();
  var currentLinks=await db.UserRoles.Where(x=>x.UserId==existing.Id).ToListAsync();db.UserRoles.RemoveRange(currentLinks);db.UserRoles.AddRange(validRoleIds.Select(roleId=>new AppUserRole{UserId=existing.Id,RoleId=roleId}));await db.SaveChangesAsync();
  await Log(id.HasValue?"UPDATE":"CREATE","AppUser",existing.Id.ToString(),$"کاربر {existing.UserName} و نقش‌های او به‌روزرسانی شد.");TempData["Result"]="اطلاعات کاربر و نقش‌های او ذخیره شد.";return RedirectToAction(nameof(Index));
 }

 [HttpPost][ValidateAntiForgeryToken]
 public async Task<IActionResult> SaveRole(int? id,string code,string title,bool isActive,int[]? permissionIds){
  code=(code??"").Trim().ToUpperInvariant();title=(title??"").Trim();if(code==""||title==""){TempData["Error"]="کد و عنوان نقش الزامی است.";return RedirectToAction(nameof(Index));}
  var role=id.HasValue?await db.Roles.FindAsync(id.Value):null;if(id.HasValue&&role==null)return NotFound();
  if(role?.IsSystem==true&&code!="SYS_ADMIN"){TempData["Error"]="کد نقش سیستمی قابل تغییر نیست.";return RedirectToAction(nameof(Index));}
  if(role?.IsSystem==true&&!isActive){TempData["Error"]="نقش سیستمی مدیر سامانه را نمی‌توان غیرفعال کرد.";return RedirectToAction(nameof(Index));}
  if(await db.Roles.AnyAsync(x=>x.Id!=(id??0)&&x.Code==code)){TempData["Error"]="کد نقش تکراری است.";return RedirectToAction(nameof(Index));}
  if(role==null){role=new AppRole{Code=code,Title=title,IsActive=isActive,IsSystem=false};db.Roles.Add(role);await db.SaveChangesAsync();}else{role.Code=code;role.Title=title;role.IsActive=isActive;await db.SaveChangesAsync();}
  var selected=(permissionIds??[]).Distinct().ToHashSet();var valid=await db.Permissions.Where(x=>selected.Contains(x.Id)).Select(x=>x.Id).ToListAsync();var old=await db.RolePermissions.Where(x=>x.RoleId==role.Id).ToListAsync();db.RolePermissions.RemoveRange(old);db.RolePermissions.AddRange(valid.Select(permissionId=>new AppRolePermission{RoleId=role.Id,PermissionId=permissionId}));await db.SaveChangesAsync();
  await Log(id.HasValue?"UPDATE":"CREATE","AppRole",role.Id.ToString(),$"نقش {role.Title} و دسترسی‌های آن به‌روزرسانی شد.");TempData["Result"]="نقش و سطح دسترسی آن ذخیره شد.";return RedirectToAction(nameof(Index));
 }

 public record UserRow(int Id,string UserName,string DisplayName,bool IsAdmin,bool IsActive,List<int> RoleIds,List<string> RoleTitles);
 public record UsersVm(List<UserRow> Users,List<AppRole> Roles,List<AppPermission> Permissions,Dictionary<int,List<int>> RolePermissionIds);
}