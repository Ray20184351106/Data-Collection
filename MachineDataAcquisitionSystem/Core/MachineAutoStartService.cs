using System;
using System.Collections.Generic;
using System.Linq;

namespace MachineDataAcquisitionSystem.Core
{
    // 在主界面 UI 线程调用，每个程序实例只执行一次默认启动。
    public sealed class MachineAutoStartService
    {
        private bool _started;

        public void StartOnce(
            IEnumerable<int> selectedMachineIds,
            IEnumerable<int> availableMachineIds,
            Action<int> startMachine,
            Action<int, Exception> reportFailure)
        {
            if (startMachine == null) throw new ArgumentNullException(nameof(startMachine));
            if (reportFailure == null) throw new ArgumentNullException(nameof(reportFailure));
            if (_started) return;
            _started = true;

            var available = new HashSet<int>(availableMachineIds ?? Enumerable.Empty<int>());
            foreach (int machineId in (selectedMachineIds ?? Enumerable.Empty<int>())
                .Where(id => id >= 1 && id <= 6 && available.Contains(id)).Distinct().OrderBy(id => id))
            {
                try
                {
                    startMachine(machineId);
                }
                catch (Exception ex)
                {
                    reportFailure(machineId, ex);
                }
            }
        }
    }
}
