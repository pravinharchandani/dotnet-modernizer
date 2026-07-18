using System;
using System.Collections.Generic;

namespace LegacyShop.Core
{
    [Serializable]
    public class Order
    {
        public int Id { get; set; }

        public string CustomerName { get; set; }

        public DateTime PlacedOn { get; set; }

        public List<Product> Lines { get; set; } = new List<Product>();
    }
}
