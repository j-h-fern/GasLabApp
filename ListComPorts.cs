using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    public static class ListComPorts
    {

        public static List<string> GetComPorts() {

            List<string> comPorts = new List<string>();
            var ports = SerialPort.GetPortNames(); // e.g., ["COM3", "COM12"]
            foreach (var p in ports)
                comPorts.Add(p);



            return comPorts; }
    }
}
