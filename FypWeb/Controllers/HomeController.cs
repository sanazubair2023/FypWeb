using FypWeb.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace FypWeb.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return User.GetRole() switch
                {
                    "Client" => RedirectToAction("Dashboard", "Client"),
                    "Worker" => RedirectToAction("Dashboard", "Worker"),
                    "Company" => RedirectToAction("Directory", "Company"),
                    "Police" => RedirectToAction("Portal", "Police"),
                    _ => RedirectToAction("Login", "Account")
                };
            }
            return RedirectToAction("Login", "Account");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
