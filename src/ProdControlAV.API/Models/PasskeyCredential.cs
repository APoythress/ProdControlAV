using System;

namespace ProdControlAV.API.Models;

public class PasskeyCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Credential ID and public key from WebAuthn
    public byte[] DescriptorId { get; set; } = default!;
    public byte[] PublicKey { get; set; } = default!;
    public uint SignatureCounter { get; set; }
    public string CredType { get; set; } = default!;
    public Guid AaGuid { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public AppUser User { get; set; } = default!;
}
