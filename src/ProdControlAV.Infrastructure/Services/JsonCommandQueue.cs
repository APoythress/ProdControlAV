using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.Infrastructure.Services;

public class JsonCommandQueue : ICommandQueue
{
    private readonly string _storagePath;

    public JsonCommandQueue(string storagePath)
    {
        _storagePath = storagePath;
    }

    public async Task<IEnumerable<string>> FetchPendingCommandsAsync(string deviceId)
    {
        string file = Path.Combine(_storagePath, $"{deviceId}.json");
        if (!File.Exists(file))
            return Enumerable.Empty<string>();

        string json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }
}
