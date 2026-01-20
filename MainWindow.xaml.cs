using System.IO.Ports;
using System.Runtime.CompilerServices;
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
        private string? comPort { get; set; }
        private Device? Device { get; set; }

        // 1) Start emulator on one end of the pair
        private Cpc6050Emulator emulator = new Cpc6050Emulator(
            portName: "COM29",
            baudRate: 9600,
            parity: Parity.None,
            dataBits: 8,
            stopBits: StopBits.One);
        


        public MainWindow()
        {
            InitializeComponent();
            value = 0;
            emulator.Start();
            DisplayComPorts();





        }

        private  void DebugDisplay(object sender, RoutedEventArgs e)
        {
           

            if (Device != null)
            {
                Debug_Text_Box.Clear();
                Debug_Text_Box.Text = Device.Connect();

            }
           
        }

        private void DisplayComPorts()
        {
            foreach (var p in ListComPorts.GetComPorts())
            {
                ComList.Items.Add(p);
            }
        }

        private void GetComPort(object sender, RoutedEventArgs e)
        {
            if( !string.IsNullOrEmpty(ComList.SelectedValue.ToString()))
            {
                Debug_Text_Box.Clear();
                Debug_Text_Box.Text = ComList.SelectedValue.ToString();
                comPort = ComList.SelectedValue.ToString().ToUpper();


            }
            
        }

        private async void GetConnection(object sender, RoutedEventArgs e)
        {
            Device = await Connect();
            Debug_Text_Box.Clear();
            Debug_Text_Box.Text="CONNECTED";
        }

        private Task<Device> Connect()
            // TODO ADD in A connection Timeout
        {   
            if (comPort is null)
                throw new InvalidOperationException("ComPort is null.");

           
            var sp = new SerialPortClient(comPort);
            var client = new Cpc6050Client(sp);


            Device device = new CPC6050(client, client);
            
            
            
            return Task.FromResult(device);
        }

    }
}