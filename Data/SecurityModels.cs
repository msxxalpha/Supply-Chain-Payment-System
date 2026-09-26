namespace Indamin.Payment.Data;
public class AppRole{public int Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public bool IsSystem{get;set;}public bool IsActive{get;set;}=true;}
public class AppPermission{public int Id{get;set;}public string Code{get;set;}="";public string Title{get;set;}="";public string GroupTitle{get;set;}="";public int SortOrder{get;set;}=1;}
public class AppUserRole{public int UserId{get;set;}public int RoleId{get;set;}public AppUser? User{get;set;}public AppRole? Role{get;set;}}
public class AppRolePermission{public int RoleId{get;set;}public int PermissionId{get;set;}public AppRole? Role{get;set;}public AppPermission? Permission{get;set;}}
