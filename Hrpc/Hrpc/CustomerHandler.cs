using Hrpc.Model;
using Hrpc.Services;
using HrpcCore.Attributes;

namespace Hrpc.Hrpc
{
    [HrpcController("Customer")]
    public class CustomerHandler(CustomerService service)
    {
        [HrpcMethod("AddCustomer")]
        public int AddCustomer(string name)
        {
            return service.AddCustomer(name);
        }

        [HrpcMethod("GetCustomerById")]
        // Read a customer by ID
        public Customer? GetCustomerById(int id)
        {
            return service.GetCustomerById(id);
        }

        [HrpcMethod("GetOrderByOrderId")]
        // Read a customer by ID
        public Task<List<Order>?> GetOrderByOrderId(int id, CancellationToken ct)
        {
            return Task.FromResult(service.GetOrdersById(id));
        }


        // Read all customers
        [HrpcMethod("GetAllCustomers")]
        public List<Customer> GetAllCustomers()
        {
            return service.GetAllCustomers();
        }

        // Update a customer's name
        [HrpcMethod("UpdateCustomerName")]
        public void UpdateCustomerName(int customerId, string newName)
        {
            service.UpdateCustomerName(customerId, newName);
        }

        // Delete a customer by ID

        [HrpcMethod("DeleteCustomer")]
        public void DeleteCustomer(int id)
        {
            service.DeleteCustomer(id);
        }

        // Add an address to a customer
        [HrpcMethod("AddAddressToCustomer")]
        public void AddAddressToCustomer(int customerId, string street, string city, string postalCode)
        {
            service.AddAddressToCustomer(customerId, street, city, postalCode);
        }

        // Remove an address from a customer
        [HrpcMethod("RemoveAddressFromCustomer")]
        public void RemoveAddressFromCustomer(int customerId, int addressId)
        {
            service.RemoveAddressFromCustomer(customerId, addressId);
        }

        // Add an order to a customer
        [HrpcMethod("AddOrderToCustomer")]
        public void AddOrderToCustomer(int customerId, string productName, decimal price)
        {
            service.AddOrderToCustomer(customerId, productName, price);
        }


        [HrpcMethod("RemoveOrderFromCustomer")]
        public void RemoveOrderFromCustomer(int customerId, int orderId)
        {
            service.RemoveOrderFromCustomer(customerId, orderId);
        }
    }
}
