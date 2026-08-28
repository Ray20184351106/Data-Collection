using System;

namespace MachineDataAcquisitionSystem.Core
{
    public static class LogRetentionPolicy
    {
        public static int GetRemovalLength(int currentLength, int maximumLength)
        {
            if (currentLength < 0) throw new ArgumentOutOfRangeException(nameof(currentLength));
            if (maximumLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLength));
            return Math.Max(0, currentLength - maximumLength);
        }
    }
}
