using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HashtagChris.DotNetBlueZ;
using HashtagChris.DotNetBlueZ.Extensions;
using Newtonsoft.Json.Linq;

namespace covidscan
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();
            if (adapter is null)
            {
                Console.Error.WriteLine("Could not find any bluetooth adapters.");
                return -1;
            }

            var adapterPath = adapter.ObjectPath.ToString();
            var adapterName = adapterPath.Substring(adapterPath.LastIndexOf("/") + 1);
            Console.WriteLine($"Using Bluetooth adapter {adapterName}");

            adapter.DeviceFound += OnDeviceFoundAsync;
            await adapter.StartDiscoveryAsync();
            await Task.Delay(Timeout.Infinite);

            return 0;
        }

        const string COVIDSAFE_SERVICE_UUID = "b82ab3fc-1595-4f6a-80f0-fe094cc218f9";

        static async Task OnDeviceFoundAsync(Adapter sender, DeviceFoundEventArgs eventArgs)
        {
            var device = eventArgs.Device;
            var properties = await device.GetAllAsync();
            foreach (var uuid in properties.UUIDs)
            {
                if (string.Equals(uuid, COVIDSAFE_SERVICE_UUID, StringComparison.OrdinalIgnoreCase))
                {
                    await OnCovidSafeUserFoundAsync(device);
                    return;
                }
            }
        }

        static async Task OnCovidSafeUserFoundAsync(Device device)
        {
            var timeout = TimeSpan.FromSeconds(5);
            var deviceID = device.GetDeviceID();

            // Console.WriteLine("Found COVIDSafe user: {0}", deviceID);
            await device.ConnectAsync();
            try
            {
                await device.WaitForPropertyValueAsync("Connected", value: true, timeout);
                // Console.WriteLine("Connected to {0}", deviceID);

                await device.WaitForPropertyValueAsync("ServicesResolved", value: true, timeout);

                var service = await device.GetServiceAsync(COVIDSAFE_SERVICE_UUID);
                if (service is null)
                {
                    Console.WriteLine("Failed to find COVIDSafe service.");
                    return;
                }

                var characteristic = await service.GetCharacteristicAsync(COVIDSAFE_SERVICE_UUID);
                var value =  await characteristic.ReadValueAsync(timeout);
                var message = Encoding.UTF8.GetString(value);
                var json = JObject.Parse(message);

                await OnCOVIDSafeMessageAsync(deviceID, (await device.GetAllAsync()).RSSI, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                throw;
            }
            finally
            {
                await device.DisconnectAsync();
            }
        }

        static Task OnCOVIDSafeMessageAsync(string deviceID, short rssi, JObject data)
        {
            var model = (string)data["modelP"];
            var message = (string)data["msg"];
            var org = (string)data["org"];
            var version = (int)data["v"];

            // deviceID = "XX:XX:XX:XX:XX:XX";
            // message = "REDACTEDENCRYPTEDDATAENDINGWITH" + message[^5..];

            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {deviceID}: org={org} v={version} model={model} RSSI={rssi} msg={message}");

            return Task.CompletedTask;
        }
    }

    static class Extensions
    {
        public static string GetDeviceID(this Device device)
        {
            // This could be optimized with LastIndexOf and Span etc.
            // but not really critical now.

            var deviceID = device.ObjectPath.ToString().Split('/').Last();
            if (deviceID.StartsWith("dev_"))
            {
                deviceID = deviceID[4..];
            }
            deviceID = deviceID.Replace('_', ':');
            return deviceID;
        }
    }
}
