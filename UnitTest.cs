using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    public abstract class UnitTest
    {
        public Device device;


        public UnitTest(Device _device) 
        {
            this.device = _device;
        }

    }

    public sealed class CPC6050Test:UnitTest
    {
        private string channel = "A";
        private string units = "Kpa"; 
        private string mode = "Measure"; //Measure, Control, Vent
        private string pressureType = "Abs"; // Gauge, Abs
        private double setPoint = 101.00;
        public Dictionary<string, bool> Results { get;  private set; } = new Dictionary<string, bool>(); 
        public Dictionary<string,string> Values { get; private set; } = new Dictionary<string,string>();
        public CPC6050Test(CPC6050 _device) : base(_device) 
        {
            



        }

        public void RunTest()
        {
            CPC6050 DUT = (CPC6050)device;
            Values.Clear();
            Values.Add("Mode", DUT.SetMode(PconMode.Control));
            Values.Add("Channel", DUT.SetChannel(PConChannel.A).ToLower());
            Values.Add("Ptype",DUT.SetPressureType(Ptype.Abs).ToLower());
            Values.Add("Units", DUT.SetUnits("Kpa").ToLower());
            Values.Add($"SetPoint", DUT.SetSetPoint(setPoint).ToString().ToLower());
            Values.Add("Stable", DUT.WaitStable(PConChannel.A, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(250)).ToString().ToLower());
            Values.Add("Pressure", DUT.GetReadingFromMainSensor().ToString().ToLower());
            Results.Clear();
            Results.Add("Mode", Values["Mode"] ==mode);
            Results.Add("Channel", Values["Channel"] == channel.ToLower());
            Results.Add("Ptype", Values["Ptype"]== pressureType.ToLower());
            Results.Add("Units", Values["Units"] == units.ToLower());
            Results.Add($"SetPoint", Values["SetPoint"] == setPoint.ToString().ToLower());
            Results.Add("Stable", Convert.ToBoolean(Values["Stable"]));
            Results.Add("Pressure", Values["Pressure"] == setPoint.ToString().ToLower());

            


        }


    }
}
