using SharpCoreDB.Interfaces;

public class DataSeeder
{
    public void SeedOrders(IDatabase db, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var id = (i + 1) * 100;
            var customerId = ((i % 5) * 100) + 48;
            var amount = id * 100;
            var status = "status_48";
            Console.WriteLine($"Inserting order {id}");
            var parameters = new Dictionary<string, object?>
            {
                ["0"] = id,
                ["1"] = customerId,
                ["2"] = status,
                ["3"] = amount
            };
            db.ExecuteSQL("INSERT INTO Orders (Id, CustomerId, Status, Amount) VALUES (?, ?, ?, ?)", parameters);
        }
    }
}
