using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using LegacyShop.Core;
using Newtonsoft.Json;

namespace LegacyShop.Web.Controllers
{
    public class ProductsController : Controller
    {
        private static readonly List<Product> Catalog = new List<Product>
        {
            new Product { Id = 1, Name = "Mechanical Keyboard", Price = 89.99m },
            new Product { Id = 2, Name = "Trackball Mouse", Price = 34.50m },
            new Product { Id = 3, Name = "CRT Monitor (refurbished)", Price = 120.00m },
        };

        public ActionResult Index()
        {
            return View(Catalog);
        }

        public ActionResult Details(int id)
        {
            Product product = Catalog.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return HttpNotFound();
            }

            return View(product);
        }

        public ContentResult Export()
        {
            string json = JsonConvert.SerializeObject(Catalog, Formatting.Indented);
            return Content(json, "application/json");
        }
    }
}
