using System;
using System.Collections.Generic;
using System.Linq;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Models;
using Newtonsoft.Json;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests
{
    public sealed class MachineAutoStartServiceTests
    {
        [Fact]
        public void Starts_only_selected_available_supported_machines_once()
        {
            var started = new List<int>();
            var service = new MachineAutoStartService();

            service.StartOnce(new[] { 5, 2, 2, 3, 0, 7 }, new[] { 1, 2, 5, 7 },
                started.Add, (id, error) => throw error);
            service.StartOnce(new[] { 1, 2, 5 }, new[] { 1, 2, 5 },
                started.Add, (id, error) => throw error);

            Assert.Equal(new[] { 2, 5 }, started);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"AutoStart\":true,\"AutoStartMonitor\":true}")]
        [InlineData("{\"AutoStartMachineIds\":null}")]
        public void Old_or_empty_settings_do_not_implicitly_start_any_machine(string json)
        {
            var settings = JsonConvert.DeserializeObject<AppSettings>(json);
            var started = new List<int>();

            new MachineAutoStartService().StartOnce(settings.AutoStartMachineIds, Enumerable.Range(1, 6),
                started.Add, (id, error) => throw error);

            Assert.Empty(started);
        }

        [Fact]
        public void Starts_selected_machines_even_when_windows_auto_start_is_disabled()
        {
            var settings = new AppSettings { AutoStart = false, AutoStartMachineIds = new List<int> { 3 } };
            var started = new List<int>();

            new MachineAutoStartService().StartOnce(settings.AutoStartMachineIds, Enumerable.Range(1, 6),
                started.Add, (id, error) => throw error);

            Assert.Equal(new[] { 3 }, started);
        }

        [Fact]
        public void One_start_failure_is_reported_without_blocking_later_machines_or_retrying()
        {
            var attempted = new List<int>();
            var failures = new List<int>();
            var failure = new InvalidOperationException("test start failure");
            var service = new MachineAutoStartService();
            Action<int> start = id =>
            {
                attempted.Add(id);
                if (id == 2) throw failure;
            };
            Action<int, Exception> report = (id, error) =>
            {
                Assert.Same(failure, error);
                failures.Add(id);
            };

            service.StartOnce(new[] { 2, 4 }, new[] { 2, 4 }, start, report);
            service.StartOnce(new[] { 2, 4 }, new[] { 2, 4 }, start, report);

            Assert.Equal(new[] { 2, 4 }, attempted);
            Assert.Equal(new[] { 2 }, failures);
        }
    }
}
