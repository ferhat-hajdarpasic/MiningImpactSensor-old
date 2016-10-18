using MiningImpactSensor.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace MiningImpactSensor.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class DevicePage : Page
    {
        DispatcherTimer _timer;
        SensorTag sensor;
        bool registeredConnectionEvents;
        ObservableCollection<TileModel> tiles = new ObservableCollection<TileModel>();

        public DevicePage()
        {
            this.InitializeComponent();

            Clear();

        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            sensor = e.Parameter as SensorTag;

            SensorList.ItemsSource = tiles;

            base.OnNavigatedTo(e);
            await this.ConnectSensors();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            active = false;
            if (sensor != null && e.NavigationMode == NavigationMode.Back)
            {
                sensor.Disconnect();
            }
        }

        public async Task RegisterAccelerometer(bool register)
        {
            try
            {
                if (register)
                {
                    await sensor.Accelerometer.StartReading();
                    sensor.Accelerometer.AccelerometerMeasurementValueChanged -= OnAccelerometerMeasurementValueChanged;
                    sensor.Accelerometer.AccelerometerMeasurementValueChanged += OnAccelerometerMeasurementValueChanged;
                    AddTile(new TileModel() { Caption = "Accelerometer", Icon = new BitmapImage(new Uri("ms-appx:/Assets/Accelerometer.png")) });
                }
                else
                {
                    RemoveTiles(from t in tiles where t.Caption == "Accelerometer" select t);
                    await sensor.Accelerometer.StopReading();
                    sensor.Accelerometer.AccelerometerMeasurementValueChanged -= OnAccelerometerMeasurementValueChanged;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("### Error registering Accelerometer: " + ex.Message);
            }
        }

        public void RegisterButtons(bool register)
        {
            try
            {
                if (register)
                {
                    sensor.Buttons.ButtonValueChanged -= OnButtonValueChanged;
                    sensor.Buttons.ButtonValueChanged += OnButtonValueChanged;
                    tiles.Add(new TileModel() { Caption = "Buttons", Icon = new BitmapImage(new Uri("ms-appx:/Assets/Buttons.png")) });
                }
                else
                {
                    RemoveTiles(from t in tiles where t.Caption == "Humidity" select t);
                    sensor.Buttons.ButtonValueChanged -= OnButtonValueChanged;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("### Error registering Humidity: " + ex.Message);
            }
        }

        public async Task RegisterEvents(bool register)
        {
            // these ones we always listen to.
            if (!registeredConnectionEvents)
            {
                registeredConnectionEvents = true;
                sensor.ServiceError += OnServiceError;
                sensor.StatusChanged += OnStatusChanged;
                sensor.ConnectionChanged += OnConnectionChanged;
            }

            if (sensor.Version == 1)
            {
                await RegisterAccelerometer(register);
                RegisterButtons(register);
            }
        }

        void OnButtonValueChanged(object sender, SensorButtonEventArgs e)
        {
            string caption = "";
            if (e.LeftButtonDown)
            {
                caption += "Left ";
            }
            if (e.RightButtonDown)
            {
                caption += "Right";
            }
            var nowait = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, new DispatchedHandler(() =>
            {
                GetTile("Buttons").SensorValue = caption;
            }));
        }

        void OnStatusChanged(object sender, string status)
        {
            DisplayMessage(status);
        }

        double Fahrenheit(double celcius)
        {
            return celcius * 1.8 + 32.0;
        }


        string FormatTemperature(double t)
        {
            if (!Settings.Instance.Celcius)
            {
                t = Fahrenheit(t);
            }
            return t.ToString("N2");
        }

        private void OnAccelerometerMeasurementValueChanged(object sender, AccelerometerMeasurementEventArgs e)
        {
            var nowait = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
            {
                var m = e.Measurement;
                string caption = Math.Round(m.X, 3) + "," + Math.Round(m.Y, 3) + "," + Math.Round(m.Z, 3);
                GetTile("Accelerometer").SensorValue = caption;
                connected = true;
            }));
        }

        private TileModel GetTile(string name)
        {
            return (from t in tiles where t.Caption == name select t).FirstOrDefault();
        }

        void OnServiceError(object sender, string message)
        {
            DisplayMessage(message);
        }

        private void Clear()
        {
            foreach (var tile in tiles)
            {
                tile.SensorValue = "";
            }
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        public async void Show()
        {
            if (!active)
            {
                Clear();
                active = true;
                await ConnectSensors();
            }
        }

        bool connecting;

        private async Task ConnectSensors()
        {
            try
            {
                if (sensor == null)
                {
                    // no paired SensorTag, tell the user 
                    DisplayMessage("This page should be navigated to with a SensorTag parameter");
                    return;
                }
                if (connecting)
                {
                    return;
                }
                connecting = true;
                if (sensor.Connected || await sensor.ConnectAsync())
                {
                    connected = true;
                    await RegisterEvents(true);
                    if (sensor.Accelerometer != null)
                    {
                        await sensor.Accelerometer.SetPeriod(1000); // save battery
                    }
                    SensorList.ItemsSource = tiles;
                }

            }
            catch (Exception ex)
            {
                DisplayMessage("Connect failed, please ensure sensor is on and is not in use on another machine.");
                Debug.WriteLine(ex.Message);
            }
            connecting = false;
        }

        private void StartTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer();
                _timer.Interval = TimeSpan.FromSeconds(10);
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }
        }

        private void StopTimer()
        {
            if (_timer != null)
            {
                _timer.Tick -= OnTimerTick;
                _timer.Stop();
                _timer = null;
            }
        }


        void OnTimerTick(object sender, object e)
        {
            try
            {

            }
            catch (Exception)
            {
            }
        }

        private void DisplayMessage(string message)
        {
            var nowait = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
            {
                ErrorMessage.Text = message;
            }));
        }

        bool active;
        bool connected;

        void OnConnectionChanged(object sender, ConnectionChangedEventArgs e)
        {
            if (e.IsConnected != connected)
            {
                string message = null;
                if (e.IsConnected)
                {
                    message = "connected";
                }
                else if (connected)
                {
                    message = "lost connection";
                }

                if (!e.IsConnected)
                {
                    var nowait = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new Windows.UI.Core.DispatchedHandler(() =>
                    {
                        Clear();
                    }));
                }

                DisplayMessage(message);
            }
            connected = e.IsConnected;
        }

        public async void OnVisibilityChanged(bool visible)
        {
            if (visible)
            {
                if (!active)
                {
                    active = true;
                    await ConnectSensors();
                }
            }
            else
            {
                // we are leaving the app, so disconnect the bluetooth services so other apps can use them.
                active = false;
                if (sensor != null)
                {
                    sensor.Disconnect();
                }
            }
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            TileModel tile = e.ClickedItem as TileModel;
            if (tile != null)
            {
                SelectTile(tile);
            }
        }

        private void SelectTile(TileModel model)
        {
            Frame frame = Window.Current.Content as Frame;

            switch (model.Caption)
            {
                case "Accelerometer":
                    frame.Navigate(typeof(AccelerometerPage));
                    break;
                case "Buttons":
                    frame.Navigate(typeof(ButtonPage));
                    break;
            }
        }


        private void AddTile(TileModel model)
        {
            if (!(from t in tiles where t.Caption == model.Caption select t).Any())
            {
                tiles.Add(model);
            }
        }

        private void RemoveTiles(IEnumerable<TileModel> enumerable)
        {
            foreach (TileModel tile in enumerable.ToArray())
            {
                tiles.Remove(tile);
            }
        }

        private void OnGoBack(object sender, RoutedEventArgs e)
        {
            Frame.GoBack();
        }
    }
}
