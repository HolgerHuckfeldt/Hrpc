using Hrpc.Model;
using Hrpc.Services;
using HrpcCore.Attributes;

namespace Hrpc.Hrpc
{
    [HrpcController("Customer")]
    public class CustomerHandler(CustomerService service)
    {
        [HrpcMethod("AddCustomer")]
        public async Task<int> AddCustomer(string name)
        {
            return await service.AddCustomer(name);
        }

        [HrpcMethod("GetCustomerById")]
        // Read a customer by ID
        public async Task<Customer?> GetCustomerById(int id)
        {
            return await service.GetCustomerById(id);
        }

        [HrpcMethod("GetOrderByOrderId")]
        // Read a customer by ID
        public async Task<List<Order>?> GetOrderByOrderId(int id, CancellationToken ct)
        {
            return await service.GetOrdersById(id);
        }


        // Read all customers
        [HrpcMethod("GetAllCustomers")]
        public async Task<List<Customer>> GetAllCustomers()
        {
            return await service.GetAllCustomers();
        }

        // Update a customer's name
        [HrpcMethod("UpdateCustomerName")]
        public async Task UpdateCustomerName(int customerId, string newName)
        {
            await service.UpdateCustomerName(customerId, newName);
        }

        // Delete a customer by ID

        [HrpcMethod("DeleteCustomer")]
        public async Task DeleteCustomer(int id)
        {
            await service.DeleteCustomer(id);
        }

        // Add an address to a customer
        [HrpcMethod("AddAddressToCustomer")]
        public async Task AddAddressToCustomer(int customerId, string street, string city, string postalCode)
        {
            await service.AddAddressToCustomer(customerId, street, city, postalCode);
        }

        // Remove an address from a customer
        [HrpcMethod("RemoveAddressFromCustomer")]
        public async Task  RemoveAddressFromCustomer(int customerId, int addressId)
        {
            await service.RemoveAddressFromCustomer(customerId, addressId);
        }

        // Add an order to a customer
        [HrpcMethod("AddOrderToCustomer")]
        public async Task AddOrderToCustomer(int customerId, string productName, decimal price)
        {
            await service.AddOrderToCustomer(customerId, productName, price);
        }

        [HrpcMethod("RemoveOrderFromCustomer")]
        public async Task RemoveOrderFromCustomer(int customerId, int orderId)
        {
            await service.RemoveOrderFromCustomer(customerId, orderId);
        }
    }
}
