using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quobject.EngineIoClientDotNet.Modules;
using Quobject.SocketIoClientDotNet.Client;

namespace Dealyze.SocketIO
{
    public static class DealyzeEvents
    {
        public const string Ready = "ready";
        public const string Customer = "customer";
        public const string Order = "order";
    }

    public class DealyzeClient : IDisposable
    {
        protected Socket _socket;
        protected dynamic _callback;

        public string Uri { get; set; } = "ws://localhost:3100";
        public bool EnableLogging
        {
            get { return LogManager.Enabled; }
            set { LogManager.Enabled = value; }
        }

        public string LogFile { get; set; } = "dealize_error.log";
        public string LogPayloadJsonFile { get; set; } = "payload.json";

        // Flatten things to make it easy to call from VFP
        public string EmployeeId { get; set; } = "";
        public string EmployeeUsername { get; set; } = "";
        public Employee CurrentEmployee
        {
            get
            {
                return new Employee()
                {
                    ID = EmployeeId,
                    Username = EmployeeUsername
                };
            }
        }

        public Customer CurrentCustomer { get; set; } = null;

        protected List<OrderItem> _orderLines = new List<OrderItem>();
        protected List<Discount> _discountLines = new List<Discount>();

        public void Register(dynamic callback)
        {
            _callback = callback;
        }

        public void Connect()
        {
            _socket = IO.Socket(Uri);            

            HookupMessageEvents();
        }
        
        public void ClearOrderLines()
        {
            _orderLines.Clear();
        }

        public void ClearDiscounts()
        {
            _discountLines.Clear();
        }

        public void AddOrderLine(string sku, string name, decimal price)
        {
            _orderLines.Add(new OrderItem() { Skus = new List<string> { sku.TrimEnd() }, Name = name.TrimEnd(), Price = price });
        }

        public void AddDiscountLine(string sku, string name, decimal amount, decimal percent)
        {
            _discountLines.Add(new Discount() { Name = name, Skus = new List<string> { sku }, Percent = percent });
        }

        /*
         * Sends an employee account to sign into Dealyze 
         */
        public void SendEmployee()
        {
            if (CurrentEmployee == null)
            {
                Log("Cannot send employee, CurrentEmployee is null");
                return;
            }

            var payload = new EmployeePayload();
            payload.Employee = CurrentEmployee;

            var json = JsonConvert.SerializeObject(payload);
            Log(LogPayloadJsonFile, json, raw: true);

            System.Console.WriteLine("Sending employee and waiting for messages.");
            var emitter = _socket.Emit("employee", JObject.FromObject(payload));
        }

        /*
         * Sends a bill pay (make sure to send the real price and SKU)
         */
        public void PayBill()
        {
            if (CurrentCustomer == null)
            {
                Log("Cannot redeem reward, CurrentCustomer is null");
                return;
            }

            if (CurrentEmployee == null)
            {
                Log("Cannot redeem reward, CurrentEmployee is null");
                return;
            }

            try
            {
                var item = new OrderItem();
                item.Name = "Bill Pay";
                item.Skus = new List<string>() { "abc123" };
                item.Price = 12.5M;

                var order = new Order();
                order.Items = new List<OrderItem>() { item };

                var payload = new BillPayPayload();
                payload.Employee = CurrentEmployee;
                payload.Order = order;

                var json = JsonConvert.SerializeObject(payload);
                Log(LogPayloadJsonFile, json, raw: true);

                var emitter = _socket.Emit("order", JObject.FromObject(payload));
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        /*
         * After the user chooses to redeem a reward, sends back the correct 
         * Discount object with the real reward SKU. (The test code just sends
         * the same discount object received from Dealyze, but the real code
         * should send the correct Item and Discount)       
         */
        public void RedeemReward(decimal total)
        {
            if (CurrentCustomer == null)
            {
                Log("Cannot redeem reward, CurrentCustomer is null");
                return;
            }

            if (CurrentEmployee == null)
            {
                Log("Cannot redeem reward, CurrentEmployee is null");
                return;
            }

            try
            {
                var order = new Order()
                {
                    Items = _orderLines,
                    Discounts = _discountLines,
                    Total = total
                };

                var payload = new RedeemPayload();
                payload.Employee = CurrentEmployee;
                payload.Order = order;
                payload.Customer = CurrentCustomer;

                var json = JsonConvert.SerializeObject(payload);
                Log(LogPayloadJsonFile, json, raw: true);

                var emitter = _socket.Emit("order", JObject.FromObject(payload));
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        protected void Log(string msg)
        {
            Log(LogFile, msg);            
        }

        protected void Log(string file, string msg, bool raw = false)
        {
            if (EnableLogging)
            {
                try
                {
                    string logMsg = raw ? msg : DateTime.Now.ToString() + " - " + (msg ?? "(null)") + Environment.NewLine;

                    System.IO.File.WriteAllText(file,
                                                logMsg);
                }
                catch (Exception) { }                
            }
        }

        /// <summary>
        /// Hook up callback events     
        /// </summary>
        protected void HookupMessageEvents()
        {
            _socket.On(Socket.EVENT_CONNECT, () =>
            {
                OnConnect();
            });

            _socket.On(Socket.EVENT_DISCONNECT, (reason) =>
            {
                OnDisconnect(reason?.ToString());
            });

            _socket.On(DealyzeEvents.Ready, () =>
            {
                OnReady();
            });

            _socket.On(DealyzeEvents.Customer, (msg) =>
            {
                OnCustomer(msg?.ToString());
            });

            _socket.On(DealyzeEvents.Order, (msg) =>
            {
                OnOrder(msg?.ToString());
            });
        }

        /*
         * Event received on connection, standard Socket.IO event
         */       
        public void OnConnect()
        {
            try
            {
                if (_callback != null)
                    _callback.OnConnect();
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        /*
         * Event received on disconnection, standard Socket.IO event
         *        
         * If the reason is due to the Dealyze app closing, we call Connect
         * immediately to automatically reconnect once it comes back online
         */       
        public void OnDisconnect(string reason)
        {
            try
            {
                if (_callback != null)
                    _callback.OnDisonnect(reason);
            }                      
            catch (Exception ex)
            {
                Log(ex.Message);
            }

            // The disconnection was initiated by the Dealyze Register, so call 
            // Connect to wait for it to come back online and reconnect automatically
            if (reason == "io server disconnect")
            {
                Connect();
            }
        }

        /*
         * Event received when the Dealyze app is ready to begin communication.
         * This should be received directly after the CONNECT event.      
         */
        public void OnReady()
        {
            // When we are ready, send the employee to sign in on the Dealyze Register
            SendEmployee();
        }

        /*
         * Event received when a customer signs in, providing their phone number 
         * (and eventually other customer info)       
         */
        public void OnCustomer(string msg)
        {
            try
            {
                // Save current customer
                var payload = JsonConvert.DeserializeObject<CustomerPayload>(msg);
                if (payload == null || payload.Customer == null)
                {
                    Log("Dealyze Register returned a null customer");
                }
                else
                {
                    CurrentCustomer = payload.Customer;
                }

                if (_callback != null)
                    _callback.OnCustomer(msg);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        /*
         * Event recieved when a user attempts to redeem a reward or accept a promotion
         */
        public void OnOrder(string msg)
        {
            try
            {
                // Parse available rewards
                var settings = new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        Console.WriteLine("Error deserializing: " + args.ToString());
                        if (System.Diagnostics.Debugger.IsAttached)
                        {
                            System.Diagnostics.Debugger.Break();
                        }
                    }
                };
                var payload = JsonConvert.DeserializeObject<OrderPayload>(msg, settings);
                if (payload == null || payload.Order == null || payload.Order.Discounts == null)
                {
                    Console.WriteLine("payload: " + JsonConvert.SerializeObject(payload));
                    Console.WriteLine("Failed to parse order payload");
                    Log("Dealyze Register returned no discounts");
                }
                else
                {
                    _discountLines = payload.Order.Discounts;
                }

                if (_callback != null)
                    _callback.OnOrder(msg);
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                // Free unmanaged resources (unmanaged objects)
                _callback = null;

                disposedValue = true;
            }
        }

        ~DealyzeClient()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    /*
     * Objects sent to and received from Dealyze, used for de/serialization
     */   

    public class Employee
    {
        [JsonProperty("id")]
        public string ID { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }
    }

    public class Customer
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("phoneNumber")]
        public string PhoneNumber { get; set; }

        [JsonProperty("emailAddress")]
        public string EmailAddress { get; set; }
    }

    public class Order
    {
        [JsonProperty("items")]
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();

        [JsonProperty("total")]
        public decimal Total { get; set; }

        [JsonProperty("discounts")]
        public List<Discount> Discounts { get; set; }
    }

    public class OrderItem
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("skus")]
        public List<string> Skus { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }
    }

    public class Discount
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("skus")]
        public List<string> Skus { get; set; }

        [JsonProperty("percent")]
        public decimal Percent { get; set; }
    }

    /*
     * Payload objects received from Dealyze, used for deserialization
     */   

    public class OrderPayload
    {
        [JsonProperty("order")]
        public Order Order { get; set; }
    }

    public class EmployeePayload
    {
        [JsonProperty("employee")]
        public Employee Employee { get; set; }
    }

    public class CustomerPayload
    {
        [JsonProperty("customer")]
        public Customer Customer { get; set; }
    }

    public class BillPayPayload
    {
        [JsonProperty("employee")]
        public Employee Employee { get; set; }

        [JsonProperty("order")]
        public Order Order { get; set; }
    }

    public class RedeemPayload
    {
        [JsonProperty("employee")]
        public Employee Employee { get; set; }

        [JsonProperty("order")]
        public Order Order { get; set; }

        [JsonProperty("customer")]
        public Customer Customer { get; set; }
    }
}
