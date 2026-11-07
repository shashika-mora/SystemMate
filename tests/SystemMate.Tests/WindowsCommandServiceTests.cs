using SystemMate.Services;
using Xunit;

namespace SystemMate.Tests;

public sealed class WindowsCommandServiceTests
{
    [Fact]
    public async Task UnknownToolIsRejectedWithoutStartingAProcess()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new WindowsCommandService().RunAsync("custom-shell-command"));
    }

    [Theory]
    [InlineData("network-info")]
    [InlineData("component-repair")]
    public async Task CancelledRequestDoesNotLaunchEvenAnAdministratorTool(string id)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsCommandService().RunAsync(id, cancelled.Token));
    }
}
