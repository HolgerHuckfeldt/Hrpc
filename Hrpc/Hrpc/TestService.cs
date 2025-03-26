using HrpcCommon.Attribute;
using HrpcCore.Attributes;

namespace Hrpc.Hrpc
{
    [HrpcController("TestService")]
    public class TestService
    {
        [HrpcMethod("GetAdresse")]
        public AdresseX? GetAdresse(int id, string greet, CancellationToken ct)
        {
            switch (id)
            {
                case 1:
                    return new AdresseX()
                    {
                        Id = 1,
                        Name = "A",
                        Age = 1,
                        Greet = greet
                    };
                case 2:
                    return new AdresseX()
                    {
                        Id = 2,
                        Name = "B",
                        Age = 2,
                        Greet = greet
                    };
            }

            return null;
        }

        [HrpcMethod("GetAdresses")]
        public List<AdresseX> GetAdresses(CancellationToken ct)
        {
            return new List<AdresseX>()
            {
                GetAdresse(1,"", ct), GetAdresse(2,"", ct)
            };
        }

        [HrpcMethod("AddAdresse")]
        public Task<bool> AddAdresse(AdresseX? a, int r)
        {
            if (a == null)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }


        [HrpcMethod("GetAdresseParallel")]
        public async Task<AdresseX?> GetAdresseParallel(int id, string greet, CancellationToken ct)
        {
            await Task.Delay(1000);
            switch (id)
            {
                case 1:
                    return new AdresseX()
                    {
                        Id = 1,
                        Name = "A",
                        Age = 1,
                        Greet = greet
                    };
                case 2:
                    return new AdresseX()
                    {
                        Id = 2,
                        Name = "B",
                        Age = 2,
                        Greet = greet
                    };
            }


            return null;
        }
    }

    [HrpcDataContract]
    public class AdresseX
    {
        public int Id { get; set; }
        public string Name { get; set; } = String.Empty;
        public int Age { get; set; }
        public string Greet { get; set; } = String.Empty;

        public Contact? Contace { get; set; }
    }

    [HrpcDataContract]
    public class Contact
    {
        public int Id { get; set; }
        public string Name { get; set; } = String.Empty;

    }

}
