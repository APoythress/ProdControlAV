// csharp
using Azure;
using Azure.Data.Tables;
using System;

namespace ProdControlAV.Infrastructure.Services
{
    public class AtemStateEntity : ITableEntity
    {
        // Table identity
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; } = ETag.All;

        // Stored payload
        public DateTimeOffset LastUpdatedUtc { get; set; }
        public string InputsJson { get; set; } = string.Empty;
        public string CurrentSourcesJson { get; set; } = string.Empty;
    }
}