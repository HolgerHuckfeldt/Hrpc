using global::Hrpc.Model;
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
        public int AddCustomer(string name)
        {
            var customer = new Customer(_nextCustomerId++, name);
            customer.OrderId = _nextOrderId;
            _customers.Add(customer);
            return _nextCustomerId-1;
        }

        // Read a customer by ID
        public Customer? GetCustomerById(int id)
        {
            return _customers.FirstOrDefault(c => c.Id == id);
        }

        // Read a customer by ID
        public List<Order>? GetOrdersById(int id)
        {
            if (_orders.TryGetValue(id, out var orders))
            {
                return orders;
            }

            return new List<Order>();
        }

        // Read all customers
        public List<Customer> GetAllCustomers()
        {
            return _customers;
        }

        // Update a customer's name
        public void UpdateCustomerName(int customerId, string newName)
        {
            var customer = GetCustomerById(customerId);
            if (customer != null)
            {
                customer.Name = newName;
            }
        }

        // Delete a customer by ID
        public void DeleteCustomer(int id)
        {
            _customers.RemoveAll(c => c.Id == id);
        }

        // Add an address to a customer
        public void AddAddressToCustomer(int customerId, string street, string city, string postalCode)
        {
            var customer = GetCustomerById(customerId);
            if (customer != null)
            {
                var address = new Address(_nextAddressId++, street, city, postalCode);
                customer.Addresses.Add(address);
            }
        }

        // Remove an address from a customer
        public void RemoveAddressFromCustomer(int customerId, int addressId)
        {
            var customer = GetCustomerById(customerId);
            if (customer != null)
            {
                customer.Addresses.RemoveAll(a => a.Id == addressId);
            }
        }

        // Add an order to a customer
        public void AddOrderToCustomer(int customerId, string productName, decimal price)
        {
            var customer = GetCustomerById(customerId);
            if (customer != null)
            {

                var order = new Order(_nextOrderId++, productName, price);

                if (_orders.TryGetValue(customer.OrderId, out var orderlist))
                {
                    orderlist.Add(order);
                }
                else
                {
                    _orders.TryAdd(customer.OrderId, new List<Order>() { order });

                }

            }
        }

        // Remove an order from a customer
        public void RemoveOrderFromCustomer(int customerId, int orderId)
        {
            var customer = GetCustomerById(customerId);
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
