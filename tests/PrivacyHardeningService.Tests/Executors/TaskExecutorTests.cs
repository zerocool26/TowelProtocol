using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PrivacyHardeningContracts.Models;
using PrivacyHardeningService.Executors;
using System.Text.Json;
using Xunit;

namespace PrivacyHardeningService.Tests.Executors;

public sealed class TaskExecutorTests
{
    [Fact]
    public void ParseTaskDetails_Granular_UsesSelectedValue()
    {
        // Arrange
        var logger = NullLogger<TaskExecutor>.Instance;
        var executor = new TaskExecutor(logger);

        var mechanismDetails = new
        {
            type = "ScheduledTaskConfiguration",
            taskPath = @"\Microsoft\Windows\TestTask",
            actionOptions = new
            {
                userSelectable = true,
                options = new[] 
                { 
                    new { value = "Disable", label = "Disable", description = "Disables the task" },
                    new { value = "Delete", label = "Delete", description = "Deletes the task" }
                },
                // Simulate user selection
                selectedValue = "Delete"
            }
        };

        // Act
        var result = executor.ParseTaskDetails(mechanismDetails);

        // Assert
        result.Should().NotBeNull();
        result!.TaskPath.Should().Be(@"\Microsoft\Windows\TestTask");
        result.Action.Should().Be(TaskAction.Delete);
    }

    [Fact]
    public void ParseTaskDetails_Legacy_BackwardCompatible()
    {
        // Arrange
        var logger = NullLogger<TaskExecutor>.Instance;
        var executor = new TaskExecutor(logger);

        var mechanismDetails = new
        {
            // Legacy format has no type=ScheduledTaskConfiguration usually, or different
            taskPath = @"\Microsoft\Windows\TestTask",
            action = "Disable"
        };

        // Act
        var result = executor.ParseTaskDetails(mechanismDetails);

        // Assert
        result.Should().NotBeNull();
        result!.Action.Should().Be(TaskAction.Disable);
    }
}
