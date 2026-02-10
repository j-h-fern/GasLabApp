using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GasLabApp
{
    public abstract  class Device
    {
        // Dict for storing sensors for reading 
        private Dictionary<string, Sensor> Sensors { get; }
        // if the device function as a Reference will need instance  
        public IReference? Ref { get; private set; }
        // if the device will function as a controller 
        public IController? Contr { get; private set; }

        public List<string> Units;

 









        public Device(IReference _ref, IController _contr)
        {
            Sensors = new Dictionary<string, Sensor>();
            Ref = _ref;
            Contr = _contr ;


        }

        public Device( IController _contr)
        {
            Sensors = new Dictionary<string, Sensor>();
          
            Contr = _contr;


        }

        

        public Device(IReference _ref)
        {
            Sensors = new Dictionary<string, Sensor>();
            Ref = _ref;


        }

        public void AddSensors(Sensor sensorToAdd )
        {
           
            Sensors.Add(sensorToAdd.ID.ToString(),sensorToAdd);
        }

        public Dictionary<string, string> GetReading()
        {
            Dictionary<string,string> result = new Dictionary<string,string>();
            foreach (var sensor in Sensors.Keys)
            {
                result.Add(sensor,Sensors[sensor].Read());
            }
            return result;

        }
        

        public abstract string Connect();

        public void Disconnect()
        {
            if (Ref != null)
            {
                Ref.Disconnect();
                return;
            }
            if (Contr != null)
            {

                Contr.Disconnect();
                return;
            }
            throw new InvalidOperationException("No Device to Disconnect");
        }
        


        
    }




    public abstract class PressureController : Device
    {
        
        public PressureController( IController _contr) : base(_contr)
        {

            

            
        }

    }

    public class CPC6050: PressureController
    {
        private readonly Cpc6050Client client;
        public CPC6050Monitor Monitor { get; private set; }
        
        public CPC6050( IController _contr, Cpc6050Client _client ) : base(_contr)
        {
            client = _client ?? throw new ArgumentNullException(nameof(_client));
            Units = new List<string> { "KPA", "PA","MBAR", "PSI" };
            Monitor= new CPC6050Monitor( _client );
           


        }

        public override  string Connect()
        {
            if (Contr != null) return Contr.Connect();
            else throw new InvalidOperationException("No Reference set");

        }

        private bool CheckChannelInput(string ch)
        {
            var pattern = @"^(A|B)$";
            var r = new Regex(pattern, RegexOptions.Compiled);
            if (r.IsMatch(ch)) { return true; }
            return false;
        }

        public string SetChannel(PConChannel ch)
        {

  

                client.SetChannel(ch);
                
                
            
            return client.GetChannel().ToString();
        }

        public string GetChannel() 
        {
            return client.GetChannel().ToString();

        }

        public string SetUnits(string units)
        {
            client.SetUnits(units);
            return client.GetUnits().ToString();
        }

        public string GetUnits()
        {
            return client.GetUnits().ToString();
        }

        public double SetSetPoint(double setPoint)
        { 
            client.SetSetPoint(setPoint);
            return client.GetSetPoint();
        }
        public double GetSetPoint()
        { return client.GetSetPoint(); }

        public string SetMode(PconMode mode)
        {
            client.SetMode(mode);
            return client.GetMode().ToString();
        }

        public string GetMode() { return client.GetMode().ToString(); }

        public bool WaitStable(PConChannel  channel, TimeSpan timeout, TimeSpan pollInterval)
        {
            return client.WaitStable(channel, timeout,pollInterval);
        }

        public string SetPressureType(  Ptype type)
        {
            client.SetPressureType(type);
            return client.GetPressureType().ToString();
        }
        public string GetPressureType()
        { return client.GetPressureType().ToString();}

        public double GetReadingFromMainSensor()
        {
            return client.GetReading();
        }







        


        



    }
     

}
