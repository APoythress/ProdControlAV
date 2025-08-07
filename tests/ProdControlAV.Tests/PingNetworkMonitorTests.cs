using ProdControlAV.Infrastructure.Services;
using Xunit;
using System.Threading.Tasks;

public class PingNetworkMonitorTests
{
    [Fact]
    public async Task IsDeviceOnlineAsync_ReturnsFalseForInvalidIP()
    {
        var monitor = new PingNetworkMonitor();
        var result = await monitor.IsDeviceOnlineAsync("192.0.2.1"); // TEST-NET IP
        Assert.False(result);
    }
}
