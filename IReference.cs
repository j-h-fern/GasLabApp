using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    public interface IReference
    {

        //Read sensor values
        //Read the sensors and return results 

        // This class should be used to get reading of the main sensor value from the controller
        public double GetReading();

        bool IsStable();
        // Method for checking active connection
        public string Connect();

        public void Disconnect();

        
    }






           
    
}
