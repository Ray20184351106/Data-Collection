using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MachineDataAcquisitionSystem.Models
{
    public class ParseScript
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ModelId { get; set; }
        public string FileExtension { get; set; }
        public string ScriptCode { get; set; }
        public bool IsEnabled { get; set; }
    }
}
