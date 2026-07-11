using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Models
{
    public class Statistics
    {
        public int TotalFiles { get; set; }
        public int SuccessFiles { get; set; }
        public int FailFiles { get; set; }
        public double SuccessRate { get; set; }

        public void CalculateRate()
        {
            if (TotalFiles > 0)
            {
                SuccessRate = (double)SuccessFiles / TotalFiles * 100;
            }
            else
            {
                SuccessRate = 0;
            }
        }
    }
}
