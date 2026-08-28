using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JACO.SalesDiscount.Web.Controllers;

// Sign-in is handled entirely by JACO Portal (SSO) -- unauthenticated requests here
// are redirected to Portal's login by the cookie auth middleware in Program.cs.
public sealed class AccountController : Controller
{
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
