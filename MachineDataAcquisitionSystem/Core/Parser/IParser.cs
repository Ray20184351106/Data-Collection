using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Models;

namespace MachineDataAcquisitionSystem.Core.Parser
{
    public interface IParser
    {
        bool CanParse(string filePath);
        List<TestData> Parse(string filePath, int machineId, out string errorMsg);
    }
}
