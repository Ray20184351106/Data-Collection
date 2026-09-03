using Acquisition.Contracts;
using Xunit;

namespace Acquisition.Contracts.Tests;

public sealed class PhaseOneConfigurationContractTests
{
    [Fact]
    public void Machine_configuration_is_canonical_and_sorted_by_machine_id()
    {
        var result = MachineConfigurationDocument.Create(new[]
        {
            Machine(2, "机台2", @"D:\Line\M2\Incoming", @"D:\Line\M2\Success", @"D:\Line\M2\Error"),
            Machine(1, "机台1", @"D:\Line\M1\Incoming", @"D:\Line\M1\Success", @"D:\Line\M1\Error")
        });

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(new[] { 1, 2 }, result.Machines.Select(x => x.Id));
        Assert.Contains("\"machines\"", result.PayloadJson);
        Assert.Equal(ConfigPackage.ComputeSha256(result.PayloadJson), result.PayloadSha256);
    }

    [Fact]
    public void Machine_configuration_rejects_duplicate_ids_and_nested_paths()
    {
        var result = MachineConfigurationDocument.Create(new[]
        {
            Machine(1, "机台1", @"D:\Line\M1", @"D:\Line\M1\Success", @"D:\Line\M1\Error"),
            Machine(1, "重复机台", @"D:\Line\M2\Incoming", @"D:\Line\M2\Success", @"D:\Line\M2\Error")
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("重复", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("嵌套", StringComparison.Ordinal));
    }

    [Fact]
    public void Machine_configuration_rejects_ids_outside_the_legacy_runtime_range()
    {
        var result = MachineConfigurationDocument.Create(new[]
        {
            Machine(7, "机台7", @"D:\Line\M7\Incoming", @"D:\Line\M7\Success", @"D:\Line\M7\Error")
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("1到6", StringComparison.Ordinal));
    }

    private static ManagedMachineConfiguration Machine(int id, string name, string monitor, string success, string error) => new()
    {
        Id = id,
        Name = name,
        MonitorPath = monitor,
        SuccessPath = success,
        ErrorPath = error
    };
}
