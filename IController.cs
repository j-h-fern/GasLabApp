using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    public interface IController:IReference

    {
        
        // This class should be used to get reading of the main sensor value from the controller

        public void SetSetPoint(double value);
        //Check the current Test point the unit is set to
        public double GetSetPoint();
        //Get the units the controller is Currently returning ie Kpa Degrees C RH%  mA
        public string GetUnits();
        //set the units
        public void SetUnits(string value);
        //Get the current stability status of the controller



    }
}
