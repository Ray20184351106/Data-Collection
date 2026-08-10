using System;
using System.Reflection;

namespace MachineDataAcquisitionSystem.Core
{
    /// <summary>
    /// Ensures runtime-generated models have a value for the required bigint CID key.
    /// </summary>
    public static class ModelIdentityInitializer
    {
        public static bool EnsureCid(object model, Func<long> nextId)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (nextId == null) throw new ArgumentNullException(nameof(nextId));

            PropertyInfo cidProperty = model.GetType().GetProperty("CID", BindingFlags.Public | BindingFlags.Instance);
            if (cidProperty == null) return false;
            if (cidProperty.PropertyType != typeof(long) || !cidProperty.CanRead || !cidProperty.CanWrite)
                throw new InvalidOperationException("Model CID must be a writable long property.");

            long currentValue = (long)cidProperty.GetValue(model);
            if (currentValue != 0) return false;

            long generatedValue = nextId();
            if (generatedValue <= 0)
                throw new InvalidOperationException("The generated CID must be positive.");

            cidProperty.SetValue(model, generatedValue);
            return true;
        }
    }
}
