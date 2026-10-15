using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Indamin.Payment.Controllers;
[Authorize]
public sealed class SystemGuideController : Controller
{
 public IActionResult Index(string tab="calculated")
 {
  var allowed=new[]{"calculated","resilience","dashboard","finance","supplier","prices"};
  ViewBag.ActiveTab=allowed.Contains(tab)?tab:"calculated";
  return View();
 }
}
