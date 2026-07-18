using System;
using System.Collections.Generic;
using LegacyShop.Core;
using SWM = System.Web.Mvc;

namespace LegacyShop.Web.Controllers
{
    // Uses a namespace alias for System.Web.Mvc; a semantic analyzer must still
    // resolve SWM.Controller and friends to System.Web.Mvc types.
    public class OrdersController : SWM.Controller
    {
        private readonly PricingCalculator _pricing = new PricingCalculator();

        public SWM.ActionResult Index()
        {
            var orders = new List<Order>
            {
                new Order { Id = 1001, CustomerName = "Ada Lovelace", PlacedOn = DateTime.UtcNow.AddDays(-3) },
                new Order { Id = 1002, CustomerName = "Charles Babbage", PlacedOn = DateTime.UtcNow.AddDays(-1) },
            };

            return View(orders);
        }

        public SWM.ActionResult Details(int id)
        {
            var order = new Order { Id = id, CustomerName = "Ada Lovelace", PlacedOn = DateTime.UtcNow };
            ViewBag.Total = _pricing.ApplyVolumeDiscount(199.00m, quantity: 3);
            return View(order);
        }

        [SWM.HttpPost]
        public SWM.RedirectToRouteResult Cancel(int id)
        {
            TempData["Notice"] = "Order " + id + " cancelled.";
            return RedirectToAction("Index");
        }
    }
}
