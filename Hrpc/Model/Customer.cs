using HrpcCommon.Attribute;
using HrpcCore.Attributes;

namespace Hrpc.Model
{
    using System.Collections.Generic;

    [HrpcDataContract]
    public class Customer
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Name { get; set; }

        public DateTime Created { get; set; }

        public Guid ResourceId { get; set; }

        public Dictionary<string, int>? Map { get; set; }

        public List<Address> Addresses { get; set; } = new List<Address>();

        public Customer(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    [HrpcDataContract]
    public class Address
    {
        public int Id { get; set; }
        public string Street { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }

        public Address(int id, string street, string city, string postalCode)
        {
            Id = id;
            Street = street;
            City = city;
            PostalCode = postalCode;
        }
    }

    [HrpcDataContract]
    public class Order
    {
        public int Id { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }

        public Order(int id, string productName, decimal price)
        {
            Id = id;
            ProductName = productName;
            Price = price;
        }
    }



}
