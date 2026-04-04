namespace ProdControlAV.API.Models
{
    public class AtemStateStorageModel
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public List<AtemInputDto> Inputs { get; set; } = new();
        public Dictionary<string, string> CurrentSources { get; set; } = new();
    }
}