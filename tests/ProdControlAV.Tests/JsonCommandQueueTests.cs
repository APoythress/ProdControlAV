using ProdControlAV.Infrastructure.Services;
using Xunit;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public class JsonCommandQueueTests
{
    [Fact]
    public async Task FetchPendingCommandsAsync_ReturnsExpectedCommands()
    {
        var deviceId = "testDevice";
        var commands = new[] { "cmd1", "cmd2" };
        var dir = Path.Combine(Path.GetTempPath(), "ProdControlAV_Test");
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{deviceId}.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(commands));

        var queue = new JsonCommandQueue(dir);
        var result = await queue.FetchPendingCommandsAsync(deviceId);

        Assert.NotNull(result);
        Assert.Contains("cmd1", result);
        Assert.Contains("cmd2", result);
    }
}
