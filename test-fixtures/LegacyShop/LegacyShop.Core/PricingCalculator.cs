using System;

namespace LegacyShop.Core
{
    /// <summary>
    /// Pure pricing logic with no framework dependencies.
    /// NOTE for analyzer trap testing: this comment mentions System.Web but the
    /// class never uses it — a semantic analyzer must not flag this file for it.
    /// </summary>
    public class PricingCalculator
    {
        private const decimal VolumeDiscountRate = 0.10m;

        public decimal ApplyVolumeDiscount(decimal unitPrice, int quantity)
        {
            if (unitPrice < 0)
            {
                throw new ArgumentOutOfRangeException("unitPrice");
            }

            if (quantity < 1)
            {
                throw new ArgumentOutOfRangeException("quantity");
            }

            decimal total = unitPrice * quantity;
            if (quantity >= 3)
            {
                total -= total * VolumeDiscountRate;
            }

            return decimal.Round(total, 2);
        }

        public string DescribeSerializationPolicy()
        {
            // Trap: the word below is only a string literal, not an API usage.
            return "Snapshots use BinaryFormatter for historical reasons; do not extend.";
        }
    }
}
