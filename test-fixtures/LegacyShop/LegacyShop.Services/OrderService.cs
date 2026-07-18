using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using LegacyShop.Core;

namespace LegacyShop.Services
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)]
    public class OrderService : IOrderService
    {
        private static readonly List<OrderDto> Store = new List<OrderDto>
        {
            new OrderDto { Id = 1001, CustomerName = "Ada Lovelace", Total = 224.49m },
            new OrderDto { Id = 1002, CustomerName = "Charles Babbage", Total = 89.99m },
        };

        private readonly PricingCalculator _pricing = new PricingCalculator();

        public OrderDto GetOrder(int orderId)
        {
            OrderDto order = Store.FirstOrDefault(o => o.Id == orderId);
            if (order == null)
            {
                throw new FaultException<OrderFault>(
                    new OrderFault { Reason = "Order " + orderId + " not found." },
                    new FaultReason("Order not found"));
            }

            return order;
        }

        public List<OrderDto> GetOrdersForCustomer(string customerName)
        {
            return Store
                .Where(o => string.Equals(o.CustomerName, customerName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public int SubmitOrder(OrderDto order)
        {
            if (order == null || order.Total <= 0)
            {
                throw new FaultException<OrderFault>(
                    new OrderFault { Reason = "Order total must be positive." },
                    new FaultReason("Invalid order"));
            }

            order.Id = Store.Max(o => o.Id) + 1;
            order.Total = _pricing.ApplyVolumeDiscount(order.Total, quantity: 1);
            Store.Add(order);
            return order.Id;
        }
    }
}
