using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    abstract public class Sensor
    {
        List<string> Results;
        public int ID;
        private SensorType Type;






        Sensor(int _id, SensorType type)
        {
            Results = new List<string>();
            ID = _id;
            Type = type;
            




        }

       public enum SensorType
        {
            Temperature,
            Pressure,
            RH
        }

        public string Read()
        { throw  new NotImplementedException(); }
    }

}
