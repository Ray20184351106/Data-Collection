using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Models
{
    public class TestData
    {
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string ProductSn { get; set; }
        public string TestItem { get; set; }
        public decimal TestValue { get; set; }
        public string TestResult { get; set; }
        public DateTime TestTime { get; set; }
        public string FileName { get; set; }
        public DateTime CreateTime { get; set; }
    }

}
