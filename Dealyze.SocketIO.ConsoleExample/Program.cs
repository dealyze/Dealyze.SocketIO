using System;

namespace Dealyze.SocketIO.ConsoleExample
{
    class Program
    {
        public static DealyzeClient client;

        static void Main(string[] args)
        {
            System.Console.WriteLine("Starting up client...");

            // Intantiate client
            client = new DealyzeClient();
            client.Register(new EchoMessage());
            client.EnableLogging = true;

            // Setup test employee
            client.EmployeeId = "123456";
            client.EmployeeUsername = "testusername"; // This shoud be whatever username or non-numerical identifier you use in your system

            // Connect
            client.Connect();
            System.Console.WriteLine($"Logging is {client.EnableLogging}");

            // Keep the program alive
            while (true)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
    }

    public class EchoMessage
    {
        public void OnConnect()
        {
            System.Console.WriteLine($"{DateTime.Now} - Connected");
        }

        public void OnDisconnect(string msg)
        {
            System.Console.WriteLine($"{DateTime.Now} - Disconnected: {msg}");
        }

        public void OnCustomer(string msg)
        {
            System.Console.WriteLine($"{DateTime.Now} - Customer: {msg}");

            // FOR TESTING ONLY, THE REAL CODE SHOULD ONLY SEND A BILL PAY WHEN THE EMPLOYEE RINGS UP THE CUSTOMER
            Console.WriteLine(" ");
            Console.WriteLine("Wait a moment for the customer to log in, then press any key to perform a test bill pay.");
            Console.ReadKey();
            Console.WriteLine(" ");
            Program.client.PayBill();
        }

        public void OnOrder(string msg)
        {
            System.Console.WriteLine($"{DateTime.Now} - Order: {msg}");

            // FOR TESTING ONLY, THE REAL CODE SHOULD ONLY CALL RedeemReward WHEN THE EMPLOYEE RINGS UP THE CUSTOMER
            Console.WriteLine(" ");
            Console.WriteLine("Press any key to perform a test redemption.");
            Console.ReadKey();
            Console.WriteLine(" ");
            Program.client.RedeemReward((decimal)5.0);
        }

        public void OnEvent(string msg)
        {
            System.Console.WriteLine($"{DateTime.Now} - Order: {msg}");
        }
    }

}
