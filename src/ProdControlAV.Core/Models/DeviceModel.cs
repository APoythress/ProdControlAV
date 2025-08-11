using System;

namespace ProdControlAV.Core.Models;

public class DeviceModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Model { get; set; }
    public string Brand { get; set; }
    public string Type { get; set; }
    public bool AllowTelNet { get; set; }
}