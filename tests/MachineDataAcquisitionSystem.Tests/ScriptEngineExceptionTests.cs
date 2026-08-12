using System;
using System.Reflection;
using MachineDataAcquisitionSystem.Core;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class ScriptEngineExceptionTests
    {
        [Fact]
        public void Compiled_executor_preserves_the_mapping_error_instead_of_returning_reflection_noise()
        {
            Type executorType = typeof(ScriptEngine).GetNestedType(
                "CompiledScriptExecutor",
                BindingFlags.NonPublic);
            Assert.NotNull(executorType);
            var probe = new ThrowingScriptProbe();
            MethodInfo scriptMethod = typeof(ThrowingScriptProbe).GetMethod(nameof(ThrowingScriptProbe.Execute));
            object executor = Activator.CreateInstance(
                executorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { probe, scriptMethod },
                null);

            TargetInvocationException reflectionError = Assert.Throws<TargetInvocationException>(() =>
                executorType.GetMethod("Execute").Invoke(executor, new object[] { "sample.xlsx", 1 }));

            InvalidOperationException mappingError = Assert.IsType<InvalidOperationException>(
                reflectionError.InnerException);
            Assert.Equal("Excel第6行 D6 字段StandardValue: CONVERSION_FAILED", mappingError.Message);
        }

        private sealed class ThrowingScriptProbe
        {
            public object Execute(string filePath, int machineId)
            {
                throw new InvalidOperationException(
                    "Excel第6行 D6 字段StandardValue: CONVERSION_FAILED");
            }
        }
    }
}
