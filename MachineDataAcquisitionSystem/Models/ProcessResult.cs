using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Models
{
    public class ProcessResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public string Message { get; set; }
        public int DataCount { get; set; }

        public static ProcessResult Ok(string fileName, string message, int dataCount = 0)
        {
            return new ProcessResult
            {
                Success = true,
                FileName = fileName,
                Message = message,
                DataCount = dataCount
            };
        }

        public static ProcessResult Fail(string fileName, string message)
        {
            return new ProcessResult
            {
                Success = false,
                FileName = fileName,
                Message = message
            };
        }
    }
}
