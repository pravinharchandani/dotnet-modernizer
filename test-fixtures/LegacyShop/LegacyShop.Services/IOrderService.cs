using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace LegacyShop.Services
{
    [ServiceContract(Namespace = "http://legacyshop.example.com/orders/v1")]
    public interface IOrderService
    {
        [OperationContract]
        OrderDto GetOrder(int orderId);

        [OperationContract]
        List<OrderDto> GetOrdersForCustomer(string customerName);

        [OperationContract]
        [FaultContract(typeof(OrderFault))]
        int SubmitOrder(OrderDto order);
    }

    [DataContract(Namespace = "http://legacyshop.example.com/orders/v1")]
    public class OrderDto
    {
        [DataMember]
        public int Id { get; set; }

        [DataMember]
        public string CustomerName { get; set; }

        [DataMember]
        public decimal Total { get; set; }
    }

    [DataContract(Namespace = "http://legacyshop.example.com/orders/v1")]
    public class OrderFault
    {
        [DataMember]
        public string Reason { get; set; }
    }
}
