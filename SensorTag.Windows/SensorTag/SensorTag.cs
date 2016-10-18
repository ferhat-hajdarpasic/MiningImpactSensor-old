using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace MiningImpactSensor
{
    /// <summary>
    /// This class combines all of the GATT services provided by SensorTag into one helper class.
    /// See http://processors.wiki.ti.com/index.php/CC2650_SensorTag_User's_Guide#IR_Temperature_Sensor
    /// for details on GATT services.
    /// </summary>
    public class SensorTag : INotifyPropertyChanged
    {
        public static Guid IRTemperatureServiceUuid = Guid.Parse("f000aa00-0451-4000-b000-000000000000");
        // Version 1 only
        BleButtonService _buttonService;
        BleAccelerometerService _accelService;

        // variables
        bool connected;
        bool connecting;
        bool disconnecting;
        BleGattDeviceInfo deviceInfo;
        int version;
        string deviceName;
        static SensorTag _selected;

        public int Version { get { return this.version; } }

        private SensorTag(BleGattDeviceInfo deviceInfo)
        {
            this.deviceInfo = deviceInfo;
            this.version = 1;
            string name = deviceInfo.DeviceInformation.Name;
            Debug.WriteLine("Found sensor tag: [{0}]", name);
            if (name == "CC2650 SensorTag" || name == "SensorTag 2.0")
            {
                this.version = 2;
                this.deviceName = "CC2650";
            }
            else
            {
                this.deviceName = "CC2541";
            }
        }

        /// <summary>
        /// Get or set the selected SensorTag instance.
        /// </summary>
        public static SensorTag SelectedSensor { get { return _selected; } set { _selected = value; } }


        private SensorTag()
        {
            throw new InvalidOperationException();
        }

        public string DeviceAddress
        {
            get { return deviceInfo.Address.ToString("x"); }
        }

        public string DeviceName
        {
            get { return this.deviceName; }
        }

        public bool Connected { get { return connected; } }
        
        /// <summary>
        /// Find all SensorTag devices that are paired with this PC.
        /// </summary>
        /// <returns></returns>
        public static async Task<IEnumerable<SensorTag>> FindAllDevices()
        {
            List<SensorTag> result = new List<SensorTag>();
            foreach (var device in await BleGenericGattService.FindMatchingDevices(IRTemperatureServiceUuid))
            {
                string name = "" + device.DeviceInformation.Name;
                if (name.Contains("SensorTag") || name.Contains("Sensor Tag"))
                {
                    result.Add(new SensorTag(device));
                }
            }
            return result;
        }


        public BleAccelerometerService Accelerometer { get { return _accelService; } }
        public BleButtonService Buttons { get { return _buttonService; } }

        public event EventHandler<string> StatusChanged;

        private void OnStatusChanged(string status)
        {
            if (StatusChanged != null)
            {
                StatusChanged(this, status);
            }
        }

        /// <summary>
        /// Connect or reconnect to the device.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> ConnectAsync()
        {
            if (!connecting && !connected)
            {
                disconnecting = false;

                try
                {
                    OnStatusChanged("connecting...");

                    // since all this code is async, user could quit in the middle, hence all the checks
                    // on the "disconnecting" state.
                    if (disconnecting) return false;

                    // Version 1 only
                    if (version == 1)
                    {
                        await ConnectButtonService();
                        if (disconnecting) return false;
                        await ConnectAccelerometerService();
                        if (disconnecting) return false;
                    }

                    connected = true;
                    OnStatusChanged("connected");
                }
                finally
                {
                    connecting = false;
                }
            }
            return true;
        }

        public async void Disconnect()
        {
            disconnecting = true;
            connected = false;

            if (_buttonService != null)
            {
                using (_buttonService)
                {
                    _buttonService.Error -= OnServiceError;
                    _buttonService.ConnectionChanged -= OnConnectionChanged;
                    _buttonService = null;
                }
            }
            if (_accelService != null)
            {
                using (_accelService)
                {
                    try
                    {
                        _accelService.Error -= OnServiceError;
                        _accelService.ConnectionChanged -= OnConnectionChanged;
                        await _accelService.StopReading();
                    }
                    catch { }
                    _accelService = null;
                }
            }
        }

        private async Task<bool> ConnectButtonService()
        {
            if (_buttonService == null)
            {
                _buttonService = new BleButtonService() { Version = this.version };
                _buttonService.Error += OnServiceError;

                if (await _buttonService.ConnectAsync(deviceInfo.ContainerId))
                {
                    _buttonService.ConnectionChanged += OnConnectionChanged;
                    return true;
                }
                _buttonService.Error -= OnServiceError;
                _buttonService = null;
                return false;
            }
            return true;
        }

        private async Task<bool> ConnectAccelerometerService()
        {
            if (_accelService == null)
            {
                _accelService = new BleAccelerometerService() { Version = this.version };
                _accelService.Error += OnServiceError;

                if (await _accelService.ConnectAsync(deviceInfo.ContainerId))
                {
                    _accelService.ConnectionChanged += OnConnectionChanged;
                    return true;
                }
                _accelService.Error -= OnServiceError;
                _accelService = null;
                return false;
            }
            return true;
        }

        public event EventHandler<string> ServiceError;

        private void OnServiceError(object sender, string message)
        {
            if (ServiceError != null)
            {
                ServiceError(sender, message);
            }

        }


        public event EventHandler<ConnectionChangedEventArgs> ConnectionChanged;

        void OnConnectionChanged(object sender, ConnectionChangedEventArgs e)
        {
            if (ConnectionChanged != null)
            {
                ConnectionChanged(sender, e);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
