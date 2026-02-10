using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private int value { get; set; }
        private string? comPort { get; set; } = "COM21";
        private Device? Device { get; set; }
        private List<string> AvialableDevices { get; set; } 

        private List<string> units = new List<string>();

        private CPC6050Monitor? DeviceMonitor { get; set; }

        // 1) Start emulator on one end of the pair
        private Cpc6050Emulator emulator = new Cpc6050Emulator
        (
            portName: "COM20",
            baudRate: 9600,
            parity: Parity.None,
            dataBits: 8,
            stopBits: StopBits.One
        );

        private PressureControllerDisplay? display;



        public MainWindow()
        {
            
            InitializeComponent();
            value = 0;
            AvialableDevices = new List<string>();
            AvialableDevices.Add("CPC6050EM");
            AvialableDevices.Add("CPC6050");
            
            DisplayComPorts();
            DisplayAvailableDevices();




        }

        private void DebugDisplay(object sender, RoutedEventArgs e)
        {
            try
            {

                if (Device != null)
                {

                    Debug_Text_Box.Clear();
                    Debug_Text_Box.Text = $"{Device.Connect()}\n";
                    CPC6050Test test = new CPC6050Test((CPC6050)Device);
                    test.RunTest();
                    foreach (var k in test.Values.Keys)
                    {
                        Debug_Text_Box.Text += $"{k}:{test.Values[k]}::{test.Results[k]}\n";
                    }

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            }


        }

        /// <summary>
        /// This section is for display helpers to populate list boxes and there helpers
        /// </summary>

        private void DisplayComPorts()
        {
            foreach (var p in ListComPorts.GetComPorts())
            {
                ComList.Items.Add(p);
            }
        }

        private void GetComPort(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ComList.SelectedValue.ToString()))
                {
                    Debug_Text_Box.Clear();
                    Debug_Text_Box.Text = ComList.SelectedValue.ToString();
                    comPort = ComList.SelectedValue.ToString().ToUpper();


                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private void DisplayAvailableDevices()
        {
            DeviceList.Items.Clear();
           foreach( var v in AvialableDevices  )
            {
                DeviceList.Items.Add(v);
            }    
        }






        private async void GetConnection(object sender, RoutedEventArgs e)
        {
            try
            {
                Device = await Connect();
                if (DeviceMonitor == null)
                    throw new InvalidOperationException("DeviceMonitor was not created.");

                // Bind the window to the monitor now that it exists
                DataContext = DeviceMonitor;

                // Start the async polling loop
                //await DeviceMonitor.StartAsync(TimeSpan.FromMilliseconds(200));

                display = new PressureControllerDisplay(DeviceMonitor, (CPC6050)Device);
                ControlContainer.Children.Add(display);

                Debug_Text_Box.Clear();
                Debug_Text_Box.Text = "CONNECTED & MONITORING\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        




        private Task<Device> Connect()
        {
            SerialPortClient sp ;
            IController client;
            Device device;




            if (comPort is null)
                throw new InvalidOperationException("ComPort is null.");
            if(DeviceList is null) throw new NullReferenceException(nameof(DeviceList));
            string selectedDevice = DeviceList.SelectedValue.ToString().ToUpper();
            
            switch (selectedDevice)
            {
                case "CPC6050EM":

                    emulator.Start();
                    //sp = new Cpc6050Emulator(comPort);
                    sp = new SerialPortClient(comPort);
                    client = new Cpc6050Client(sp);
                    device = new CPC6050(client, (Cpc6050Client)client);
                    DeviceMonitor = new CPC6050Monitor((Cpc6050Client)client);
                   


                    return Task.FromResult(device);


                case "CPC6050":

                    sp = new SerialPortClient(comPort);
                   
                    client = new Cpc6050Client(sp);


                    device = new CPC6050(client, (Cpc6050Client)client);

                    DeviceMonitor = new CPC6050Monitor((Cpc6050Client)client);


                    return Task.FromResult(device);


                default:
                    throw new InvalidOperationException($"selected device{selectedDevice} not found");

            }



        }


        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DeviceMonitor == null)
                {
                    MessageBox.Show("Connect first.", "Info");
                    return;
                }

                await DeviceMonitor.StartAsync(TimeSpan.FromMilliseconds(200));
                Debug_Text_Box.Clear();
                Debug_Text_Box.Text += "Started\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DeviceMonitor?.Stop();
                Debug_Text_Box.Clear();
                Debug_Text_Box.Text += "Stopped\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        protected override void OnClosed(EventArgs e)
        {
            try
            {
                DeviceMonitor?.Dispose();
                emulator?.Dispose();
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }


    


   
}
