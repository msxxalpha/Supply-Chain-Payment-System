using Indamin.Payment.Services;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;
namespace Indamin.Payment.Controllers;
[AllowAnonymous]public class BrandController(CompanySettingsService settings):Controller{
 public async Task<IActionResult> Logo(){var x=await settings.GetAsync();return x.LogoBytes is {Length:>0}?File(x.LogoBytes,x.LogoContentType??"image/png"):NotFound();}
 public async Task<IActionResult> Favicon(){var x=await settings.GetAsync();return x.FaviconBytes is {Length:>0}?File(x.FaviconBytes,x.FaviconContentType??"image/x-icon"):NotFound();}
}