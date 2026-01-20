using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GasLabApp
{
    public abstract class UnitTest
    {
        private Device device;


        public UnitTest(Device _device) 
        {
            this.device = _device;
        }

    }

    public sealed class CPC6050Test:UnitTest
    {
        public CPC6050Test(CPC6050 _device) : base(_device) 
        {


        }


    }
}
