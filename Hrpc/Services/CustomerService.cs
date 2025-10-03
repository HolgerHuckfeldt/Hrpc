using Hrpc.Model;
using System.Collections.Concurrent;

namespace Hrpc.Services
{

    public class CustomerService
    {
        private readonly List<Customer> _customers = new();
        private readonly ConcurrentDictionary<int, List<Order>> _orders = new();

        private int _nextCustomerId = 1;
        private int _nextAddressId = 1;
        private int _nextOrderId = 1;

        // Create a new customer
        public Task<int> AddCustomer(string name)
        {
            var customer = new Customer(_nextCustomerId++, name) { OrderId = _nextOrderId };
            Task.Delay(200).Wait();
            _customers.Add(customer);
            return Task.FromResult(_nextCustomerId - 1);
        }

        // Read a customer by ID
        public Task<Customer?> GetCustomerById(int id)
        {

            return Task.FromResult(_customers.FirstOrDefault(c => c.Id == id));
        }

        // Read a customer by ID
        public Task<List<Order>> GetOrdersById(int id)
        {
            if (_orders.TryGetValue(id, out var orders))
            {
                return Task.FromResult(orders);
            }

            return Task.FromResult(new List<Order>());
        }

        // Read all customers
        public Task<List<Customer>> GetAllCustomers()
        {
            return Task.FromResult(_customers);
        }

        // Update a customer's name
        public async Task UpdateCustomerName(int customerId, string newName)
        {
            var customer = await GetCustomerById(customerId);
            if (customer != null)
            {
                customer.Name = newName;
            }
        }

        // Delete a customer by ID
        public Task DeleteCustomer(int id)
        {
            _customers.RemoveAll(c => c.Id == id);

            return Task.CompletedTask;
        }

        // Add an address to a customer
        public async Task AddAddressToCustomer(int customerId, string street, string city, string postalCode)
        {
            var customer = await GetCustomerById(customerId);
            if (customer != null)
            {
                var address = new Address(_nextAddressId++, street, city, postalCode);
                customer.Addresses.Add(address);
            }
        }

        // Remove an address from a customer
        public async Task RemoveAddressFromCustomer(int customerId, int addressId)
        {
            var customer = await GetCustomerById(customerId);
            if (customer != null)
            {
                customer.Addresses.RemoveAll(a => a.Id == addressId);
            }
        }

        // Add an order to a customer
        public async Task AddOrderToCustomer(int customerId, string productName, decimal price)
        {
            var customer = await GetCustomerById(customerId);
            if (customer != null)
            {

                var order = new Order(_nextOrderId++, productName, price);

                if (_orders.TryGetValue(customer.OrderId, out var orderlist))
                {
                    orderlist.Add(order);
                }
                else
                {
                    _orders.TryAdd(customer.OrderId, [order]);

                }

            }
        }

        // Remove an order from a customer
        public async Task RemoveOrderFromCustomer(int customerId, int orderId)
        {
            var customer = await GetCustomerById(customerId);
            if (customer != null)
            {
                if (_orders.TryGetValue(customer.OrderId, out var orderlist))
                {

                    orderlist.RemoveAll(o=>o.Id == orderId);
                }


            }
        }
    }

}
