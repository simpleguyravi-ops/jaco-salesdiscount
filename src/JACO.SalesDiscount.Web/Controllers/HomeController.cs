using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JACO.SalesDiscount.Web.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "SalesDiscount");

    [AllowAnonymous]
    public IActionResult Error() => View();
}
