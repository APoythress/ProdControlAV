# Automatic Update System Setup Guide

This guide walks you through setting up the automatic update system for the ProdControlAV Agent using NetSparkle, Ed25519 signatures, and Azure Blob Storage.

## Overview

The automatic update system consists of:
- **NetSparkle** - Headless update framework integrated into the agent
- **Ed25519 signatures** - Cryptographic signing for security
- **Azure Blob Storage** - Hosting for release files and update manifests
- **GitHub Actions** - Automated build, sign, and deploy pipeline
- **Appcast manifest** - JSON file describing available updates

## Prerequisites

- .NET 8.0 SDK (for key generation)
- Azure Storage Account with Blob Storage
- GitHub repository with Actions enabled
- Python 3.11+ (for signing scripts)

## Step 1: Generate Ed25519 Keypair

The Ed25519 keypair is used to sign releases and verify updates.

### 1.1 Install NetSparkle Tools

```bash
dotnet tool install -g NetSparkleUpdater.Tools
```

### 1.2 Generate Keypair

```bash
netsparkle-generate-keys
```

This will output:
- **Private Key** (base64) - Keep this SECRET! Store in GitHub Secrets
- **Public Key** (base64) - Add to agent configuration

Example output:
```
Private Key: SGVsbG8sIHRoaXMgaXMgYSBzYW1wbGUgcHJpdmF0ZSBrZXkgZm9yIGRlbW9uc3RyYXRpb24gb25seQ==
Public Key: VGhpcyBpcyBhIHNhbXBsZSBwdWJsaWMga2V5IGZvciBkZW1vbnN0cmF0aW9uIG9ubHk=
```

### 1.3 Store Private Key Securely

**CRITICAL:** Never commit the private key to source control!

Store the private key in GitHub Secrets:
1. Go to your repository on GitHub
2. Navigate to Settings > Secrets and variables > Actions
3. Click "New repository secret"
4. Name: `NETSPARKLE_PRIVATE_KEY`
5. Value: Paste the base64-encoded private key
6. Click "Add secret"

## Step 2: Configure Azure Blob Storage

### 2.1 Create Storage Account

1. Log into Azure Portal
2. Create a new Storage Account or use existing
3. Note the storage account name (e.g., `prodcontrolavupdates`)

### 2.2 Create Container

1. In your storage account, go to "Containers"
2. Create a new container named `updates`
3. Set public access level:
   - **Blob** - Allow anonymous read access to blobs (recommended)
   - Or use SAS tokens for private access

### 2.3 Get Connection String

1. Go to "Access keys" in your storage account
2. Copy "Connection string" under key1 or key2
3. Store in GitHub Secrets as `AZURE_STORAGE_CONNECTION_STRING`

### 2.4 Configure CORS (if needed)

If agents will check for updates from different domains:
1. Go to "Resource sharing (CORS)" in your storage account
2. Add CORS rule for Blob service:
   - Allowed origins: `*`
   - Allowed methods: `GET, HEAD`
   - Allowed headers: `*`
   - Exposed headers: `*`
   - Max age: `86400`

## Step 3: Configure GitHub Secrets

Add the following secrets to your GitHub repository:

1. **NETSPARKLE_PRIVATE_KEY**
   - Base64-encoded Ed25519 private key from Step 1
   - Used to sign release ZIP files

2. **AZURE_STORAGE_CONNECTION_STRING**
   - Azure Storage account connection string from Step 2
   - Used to upload releases to blob storage

3. **AZURE_BLOB_BASE_URL**
   - Base URL to your blob storage container
   - Format: `https://<storage-account>.blob.core.windows.net`
   - Example: `https://prodcontrolavupdates.blob.core.windows.net`

To add secrets:
1. Go to repository Settings > Secrets and variables > Actions
2. Click "New repository secret"
3. Add each secret with the name and value above

## Step 4: Configure Agent

### 4.1 Update appsettings.json

Edit `src/ProdControlAV.Agent/appsettings.json` and configure the Update section:

```json
{
  "Update": {
    "Enabled": true,
    "AppcastUrl": "https://yourstorageaccount.blob.core.windows.net/updates/appcast.json",
    "Ed25519PublicKey": "YOUR_PUBLIC_KEY_FROM_STEP_1",
    "CheckIntervalSeconds": 3600,
    "AutoInstall": true
  }
}
```

**Configuration options:**
- `Enabled` - Set to `true` to enable automatic updates
- `AppcastUrl` - URL to appcast.json in your Azure Blob Storage
  - Replace `yourstorageaccount` with your actual storage account name
- `Ed25519PublicKey` - Base64-encoded public key from Step 1
- `CheckIntervalSeconds` - How often to check for updates (default: 3600 = 1 hour)
- `AutoInstall` - Automatically download and install updates (default: true)

### 4.2 Update Systemd Service (for auto-restart)

To allow the agent to restart after updates, configure systemd:

Edit `/etc/systemd/system/prodcontrolav-agent.service`:

```ini
[Service]
Restart=always
RestartSec=10
```

This ensures the agent restarts automatically after an update.

## Step 5: Test the Release Workflow

### 5.1 Manual Workflow Trigger

Test the release workflow manually:

1. Go to Actions tab in GitHub
2. Select "Agent Release Build" workflow
3. Click "Run workflow"
4. Fill in:
   - Version: `0.3.0` (or your next version)
   - Description: `Test release`
   - Critical: `false`
5. Click "Run workflow"

### 5.2 Verify Workflow

The workflow will:
1. ✅ Build the agent for linux-arm64
2. ✅ Create a ZIP file
3. ✅ Sign the ZIP with Ed25519
4. ✅ Generate appcast.json manifest
5. ✅ Upload both to Azure Blob Storage

Check the workflow logs for any errors.

### 5.3 Verify Azure Blob Storage

Check your Azure Storage container `updates`:
- Should contain: `ProdControlAV-Agent-0.3.0-linux-arm64.zip`
- Should contain: `appcast.json`

View appcast.json to verify it has correct signature and URL.

## Step 6: Deploy Updated Agent

### 6.1 Update Agent on Raspberry Pi

Deploy the agent with update configuration:

```bash
# Stop the service
sudo systemctl stop prodcontrolav-agent

# Deploy updated agent with update configuration
# (containing updated appsettings.json with Update section)
sudo cp -r /path/to/new/agent/* /opt/prodcontrolav/agent/

# Reload systemd and restart
sudo systemctl daemon-reload
sudo systemctl start prodcontrolav-agent

# Check logs to verify update service started
sudo journalctl -u prodcontrolav-agent -f
```

You should see log messages like:
```
Initializing NetSparkle update system...
Appcast URL: https://...blob.core.windows.net/updates/appcast.json
NetSparkle update system initialized successfully
```

## Step 7: Create Tagged Releases

### 7.1 Tag-Based Releases

For production releases, use Git tags:

```bash
# Create and push a version tag
git tag agent-v0.3.0
git push origin agent-v0.3.0
```

The workflow will automatically trigger and:
1. Extract version from tag (agent-v0.3.0 → 0.3.0)
2. Build and sign the release
3. Upload to Azure Blob Storage
4. Create a GitHub Release

### 7.2 Semantic Versioning

Use semantic versioning for tags:
- `agent-v0.3.0` - Patch release
- `agent-v0.4.0` - Minor release
- `agent-v1.0.0` - Major release

## Update Flow

### How Updates Work

1. **Agent Checks for Updates**
   - Every `CheckIntervalSeconds` (default: 1 hour)
   - Downloads appcast.json from Azure Blob Storage
   - Compares available version with current version

2. **Update Available**
   - Verifies Ed25519 signature
   - Downloads ZIP file from Azure Blob Storage
   - Verifies downloaded ZIP signature

3. **Update Installation**
   - Extracts ZIP to agent directory
   - Replaces old files with new files
   - Agent exits (systemd restarts it automatically)

4. **Verification**
   - Agent starts with new version
   - Logs version number on startup
   - Continues normal operation

### Update Timeline

```
T+0:00  - New release tagged and pushed to GitHub
T+0:05  - GitHub Actions builds and uploads to Azure
T+1:00  - Agent checks for updates (next scheduled check)
T+1:01  - Agent downloads and verifies update
T+1:02  - Agent installs update and restarts
T+1:03  - Agent running new version
```

## Security Considerations

### Private Key Security

- **NEVER** commit private keys to source control
- Store private key only in GitHub Secrets
- Rotate keys periodically (every 6-12 months)
- Use different keys for dev/staging/prod if needed

### Signature Verification

- All updates MUST be signed with Ed25519
- Agent will reject unsigned or invalid updates
- NetSparkle uses `SecurityMode.Strict` for maximum security

### HTTPS Only

- All downloads use HTTPS
- Azure Blob Storage serves content over HTTPS
- No insecure HTTP connections

### Access Control

- Limit who can create GitHub releases
- Protect `main` branch with required reviews
- Use Azure RBAC to control blob storage access

## Troubleshooting

### Agent Not Checking for Updates

Check logs:
```bash
sudo journalctl -u prodcontrolav-agent | grep -i update
```

Common issues:
- `Update.Enabled` is `false` in configuration
- `AppcastUrl` is incorrect or unreachable
- `Ed25519PublicKey` is missing or invalid

### Update Download Fails

Check:
- Azure Blob Storage container is publicly accessible
- ZIP file exists in blob storage
- Network connectivity from agent to Azure

### Signature Verification Fails

Check:
- Public key in agent config matches private key used to sign
- ZIP file was not modified after signing
- Signature in appcast.json is correct

### Agent Doesn't Restart After Update

Check systemd configuration:
```bash
systemctl cat prodcontrolav-agent | grep -i restart
```

Should show:
```
Restart=always
```

## Monitoring Updates

### Check Current Version

```bash
# Check agent version
/opt/prodcontrolav/agent/ProdControlAV.Agent --version

# Or check logs
sudo journalctl -u prodcontrolav-agent | grep -i version
```

### Monitor Update Activity

```bash
# Follow logs in real-time
sudo journalctl -u prodcontrolav-agent -f | grep -i update
```

Look for messages like:
- "Update detected: Version X.X.X"
- "Downloading update version X.X.X"
- "Update downloaded successfully"

## Rollback Procedure

### Rollback to Previous Version

If an update causes issues:

1. **Stop the agent**
   ```bash
   sudo systemctl stop prodcontrolav-agent
   ```

2. **Deploy previous version**
   ```bash
   # Extract previous version ZIP
   cd /tmp
   wget https://storage.blob.core.windows.net/updates/ProdControlAV-Agent-0.2.0-linux-arm64.zip
   unzip ProdControlAV-Agent-0.2.0-linux-arm64.zip -d agent-old
   
   # Replace current version
   sudo cp -r agent-old/* /opt/prodcontrolav/agent/
   ```

3. **Restart agent**
   ```bash
   sudo systemctl start prodcontrolav-agent
   ```

4. **Disable auto-updates temporarily**
   Edit appsettings.json:
   ```json
   {
     "Update": {
       "Enabled": false
     }
   }
   ```

### Remove Bad Release from Appcast

Edit appcast.json in Azure Blob Storage to remove the problematic version.

## Maintenance

### Regular Tasks

**Monthly:**
- Review update logs
- Check Azure Blob Storage usage
- Verify signature verification is working

**Quarterly:**
- Test update process end-to-end
- Review and clean old releases from blob storage
- Update documentation if needed

**Annually:**
- Rotate Ed25519 keypair
- Review security settings
- Update .NET SDK and dependencies

### Storage Cleanup

To manage blob storage costs, periodically remove old releases:

```bash
# List all files in container
az storage blob list \
  --container-name updates \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"

# Delete old release (keep last 3-5 versions)
az storage blob delete \
  --container-name updates \
  --name "ProdControlAV-Agent-0.1.0-linux-arm64.zip" \
  --connection-string "$AZURE_STORAGE_CONNECTION_STRING"
```

Update appcast.json to remove deleted versions from manifest.

## References

- [NetSparkle Documentation](https://netsparkleupdater.github.io/NetSparkle/)
- [Azure Blob Storage Documentation](https://docs.microsoft.com/en-us/azure/storage/blobs/)
- [Ed25519 Signature Scheme](https://ed25519.cr.yp.to/)
- [Semantic Versioning](https://semver.org/)

## Support

For issues with the update system:
1. Check logs: `sudo journalctl -u prodcontrolav-agent -f`
2. Verify configuration in appsettings.json
3. Test manually downloading appcast.json and ZIP file
4. Review GitHub Actions workflow logs
5. Check Azure Blob Storage access and CORS settings

---

**Last Updated:** 2024-11-21  
**Document Version:** 1.0
