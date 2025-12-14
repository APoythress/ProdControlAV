# Release Signing Scripts

This directory contains Python scripts for signing release packages and generating update manifests for the ProdControlAV Agent automatic update system.

## Scripts

### sign_zip.py

Signs a release ZIP file using Ed25519 signature for NetSparkle automatic updates.

**Usage:**
```bash
python sign_zip.py <zip_file> [private_key_base64]
```

**Arguments:**
- `zip_file` - Path to the ZIP file to sign
- `private_key_base64` - (Optional) Base64-encoded Ed25519 private key

**Environment Variables:**
- `NETSPARKLE_PRIVATE_KEY` - Base64-encoded Ed25519 private key (if not provided as argument)

**Output:**
- Prints the base64-encoded signature to stdout
- Prints file size to stderr

**Example:**
```bash
# Using environment variable
export NETSPARKLE_PRIVATE_KEY="your_base64_private_key_here"
python sign_zip.py release.zip

# Or with command line argument
python sign_zip.py release.zip "your_base64_private_key_here"
```

**Dependencies:**
```bash
pip install PyNaCl
```

### make_appcast.py

Generates an appcast.json manifest file from a template for NetSparkle automatic updates.

**Usage:**
```bash
python make_appcast.py \
  --template <template_file> \
  --version <version> \
  --url <download_url> \
  --signature <ed25519_signature> \
  --size <file_size> \
  --output <output_file> \
  [--description <description>] \
  [--pub-date <iso_date>] \
  [--critical]
```

**Arguments:**
- `--template` - Path to appcast.template.json file
- `--version` - Version number (e.g., "0.2.0")
- `--url` - URL to the release ZIP file in Azure Blob Storage
- `--signature` - Base64-encoded Ed25519 signature
- `--size` - Size of ZIP file in bytes
- `--output` - Path to output appcast.json file
- `--description` - (Optional) Description of the release
- `--pub-date` - (Optional) Publication date in ISO format (default: current UTC time)
- `--critical` - (Optional) Mark update as critical

**Environment Variables:**
- `RELEASE_VERSION` - Alternative to --version
- `RELEASE_URL` - Alternative to --url
- `RELEASE_SIGNATURE` - Alternative to --signature
- `RELEASE_SIZE` - Alternative to --size

**Example:**
```bash
python make_appcast.py \
  --template appcast.template.json \
  --version "0.2.1" \
  --url "https://storage.blob.core.windows.net/updates/agent-0.2.1.zip" \
  --signature "BASE64_SIGNATURE_HERE" \
  --size 12345678 \
  --output appcast.json \
  --description "Bug fixes and performance improvements"
```

## Workflow Integration

These scripts are used in the `.github/workflows/agent-release.yml` GitHub Actions workflow:

1. Build the agent for linux-arm64
2. Create a ZIP file
3. **sign_zip.py** - Sign the ZIP with Ed25519 private key from secrets
4. **make_appcast.py** - Generate appcast.json with signature and metadata
5. Upload ZIP and appcast.json to Azure Blob Storage

## Key Generation

To generate an Ed25519 keypair for signing:

```bash
# Install NetSparkle tools
dotnet tool install -g NetSparkleUpdater.Tools

# Generate keypair
netsparkle-generate-keys
```

This outputs:
- **Private Key** (base64) - Store in GitHub Secrets as `NETSPARKLE_PRIVATE_KEY`
- **Public Key** (base64) - Store in agent `appsettings.json` under `Update:Ed25519PublicKey`

## Security Notes

- **NEVER** commit private keys to source control
- Store private keys only in GitHub Secrets or secure key vault
- Rotate keys periodically for security
- Use different keys for different environments (dev/staging/prod)
- All signatures use Ed25519 with strict verification

## Testing Scripts Locally

### Test Signing

```bash
# Create a test ZIP
cd /tmp
echo "test content" > test.txt
zip test.zip test.txt

# Generate test keypair (or use existing)
dotnet tool install -g NetSparkleUpdater.Tools
netsparkle-generate-keys

# Sign the ZIP
export NETSPARKLE_PRIVATE_KEY="your_private_key_here"
python .github/scripts/sign_zip.py /tmp/test.zip
```

### Test Appcast Generation

```bash
# Get signature and size
SIGNATURE=$(python .github/scripts/sign_zip.py /tmp/test.zip)
SIZE=$(stat -c%s /tmp/test.zip)

# Generate appcast
python .github/scripts/make_appcast.py \
  --template appcast.template.json \
  --version "0.0.1-test" \
  --url "https://example.com/test.zip" \
  --signature "$SIGNATURE" \
  --size "$SIZE" \
  --output /tmp/test-appcast.json

# View result
cat /tmp/test-appcast.json
```

## Troubleshooting

### PyNaCl Import Error

```
ERROR: PyNaCl library not found. Install it with: pip install PyNaCl
```

**Solution:**
```bash
pip install PyNaCl
```

### Invalid Base64 Private Key

```
ERROR: Invalid base64 private key
```

**Solution:**
- Ensure the private key is properly base64-encoded
- Check for whitespace or newlines in the key
- Verify the key was generated with `netsparkle-generate-keys`

### File Not Found

```
ERROR: File not found: /path/to/file.zip
```

**Solution:**
- Verify the file path is correct
- Ensure the file exists and is readable
- Use absolute paths when possible

## References

- [NetSparkle Documentation](https://netsparkleupdater.github.io/NetSparkle/)
- [Ed25519 Signature Scheme](https://ed25519.cr.yp.to/)
- [PyNaCl Documentation](https://pynacl.readthedocs.io/)
- [Complete Setup Guide](../../AUTOMATIC-UPDATES-SETUP.md)
