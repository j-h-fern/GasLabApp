using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GasLabApp
{
    /// <summary>
    /// Interaction logic for PressureControllerDisplay.xaml
    /// This class is used to display information for pressure controller devices with the  higher level functions need to display data
    /// </summary>
    public partial class PressureControllerDisplay : UserControl, INotifyPropertyChanged, IAsyncDisposable
    {
        private CPC6050Monitor DeviceMonitor;
        private CPC6050 Device;
        private string ElapsedText { get; set; }
        private AsyncStableTimer _timer;
        private List<double> _listToAggregate = new List<double> { };

        public PressureControllerDisplay(CPC6050Monitor monitor, CPC6050 device)
        {
            InitializeComponent();
            DeviceMonitor = monitor;
            Device = device;

            foreach (var v in device.Units)
            {
                UnitList.Items.Add(v);
            }

            _timer = new AsyncStableTimer(TimeSpan.FromMilliseconds(500),

                    async (elapsed, ct) =>
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ElapsedText = elapsed.ToString();
                            StableTimerDisplay.Text = ElapsedText;  
                            OnPropertyChanged(nameof(ElapsedText));
                        });
                    });
            Start();
        }



        private async void Start()
        {
            try
            {
                
                if (DeviceMonitor == null)
                    throw new InvalidOperationException("DeviceMonitor was not created.");

                // Bind the window to the monitor now that it exists
                DataContext = DeviceMonitor;

                // Start the async polling loop
                await DeviceMonitor.StartAsync(TimeSpan.FromMilliseconds(200));




                // React to monitor state changes
                DeviceMonitor.PropertyChanged += (_, e) =>
                {
                    //Check if stable state of Device
                    if (e.PropertyName == nameof(DeviceMonitor.Stable))
                    {
                        
                        if (DeviceMonitor.Stable && !_timer.IsRunning)     
                        {
                            _timer!.Start();
                        }
                        if (!DeviceMonitor.Stable && _timer.IsRunning)     
                        {
                            _timer!.Reset();
                            _timer!.Stop();
                        }



                    }
                    // Check for change in pressure reading
                    if (e.PropertyName == nameof(DeviceMonitor.Step))
                    {
                        double sum = 0;
                        _listToAggregate.Add(DeviceMonitor.Pressure);

                        foreach (var item in _listToAggregate)
                        {
                            sum += item;
                            
                        }

                        Avg.Content =  (sum / _listToAggregate.Count).ToString();

                    }
                };
            }




            
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetPoint(object sender, RoutedEventArgs e)
        {
            try
            {
                var value = SetPointValue.Text;
                if (string.IsNullOrEmpty(value) && value.All(char.IsDigit)) throw new ArgumentException("Set point not numeric");

                if (Device == null) throw new ArgumentNullException(nameof(Device));
                Device.SetSetPoint(Convert.ToDouble(value));
                SetPointDisplay.Text = value.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void SetPtype(object sender, RoutedEventArgs e)
        {
            try
            {
                var value = PtypeList.Text;
                if (string.IsNullOrEmpty(value.ToString())) throw new ArgumentNullException(nameof(value));

                if (Device == null) throw new ArgumentNullException(nameof(Device));

                Device.SetMode(PconMode.Vent);
                Task.Delay(TimeSpan.FromSeconds(5));
                Device.SetPressureType(Cpc6050Client.GetPtypeFromString(value.ToString()));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Measure(object sender, RoutedEventArgs e)
        {

            try
            {

                if (Device == null) throw new ArgumentNullException(nameof(Device));

                Device.SetMode(PconMode.Measure);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }
        private void Control(object sender, RoutedEventArgs e)
        {
            try
            {

                if (Device == null) throw new ArgumentNullException(nameof(Device));

                Device.SetMode(PconMode.Control);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Vent(object sender, RoutedEventArgs e)
        {

            try
            {
                if (Device == null) throw new ArgumentNullException(nameof(Device));

                Device.SetMode(PconMode.Vent);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetUnits(object sender, RoutedEventArgs e)
        {
            try
            {
                var value = UnitList.Text;
                if (string.IsNullOrEmpty(value.ToString())) throw new ArgumentNullException(nameof(value));

                if (Device == null) throw new ArgumentNullException(nameof(Device));

                Device.SetUnits(value);
                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetAvg(object sender, RoutedEventArgs e)
        {
            _listToAggregate.Clear();
        }


        private void ControlClosed(object sender, CancelEventArgs e)
        {
            Device.SetMode(PconMode.Vent);
            DisposeAsync();
        }






    public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ValueTask DisposeAsync()
        {
            _timer?.Stop();
            _timer?.DisposeAsync();
            return ValueTask.CompletedTask;
        }
    }

  



}

