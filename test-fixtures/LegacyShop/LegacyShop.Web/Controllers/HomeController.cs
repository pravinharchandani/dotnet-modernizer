using System.Web.Mvc;
using LegacyShop.Utils;

namespace LegacyShop.Web.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = StringHelpers.ToTitleCase("welcome to legacy shop");
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "LegacyShop has been in business since 2009.";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Contact(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ModelState.AddModelError("message", "Message is required.");
                return View();
            }

            TempData["Confirmation"] = "Thanks, we received your message.";
            return RedirectToAction("Index");
        }
    }
}
