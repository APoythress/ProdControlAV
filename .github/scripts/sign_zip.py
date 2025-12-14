#!/usr/bin/env python3
"""
Sign a release ZIP file using Ed25519 signature.

This script signs a release ZIP file using an Ed25519 private key
for use with NetSparkle automatic updates. The signature is base64-encoded
and can be embedded in the appcast.json manifest.

Usage:
    python sign_zip.py <zip_file> <private_key_base64>

Arguments:
    zip_file: Path to the ZIP file to sign
    private_key_base64: Base64-encoded Ed25519 private key (64 bytes)

Environment Variables:
    NETSPARKLE_PRIVATE_KEY: Base64-encoded Ed25519 private key (if not provided as argument)

Output:
    Prints the base64-encoded signature to stdout

Example:
    python sign_zip.py release.zip "base64_private_key_here"
    
    # Or using environment variable:
    export NETSPARKLE_PRIVATE_KEY="base64_private_key_here"
    python sign_zip.py release.zip

Key Generation:
    To generate an Ed25519 keypair:
    1. Install NetSparkle tools: dotnet tool install -g NetSparkleUpdater.Tools
    2. Generate keypair: netsparkle-generate-keys
    3. Store the private key in GitHub Secrets as NETSPARKLE_PRIVATE_KEY
    4. Store the public key in agent appsettings.json Update:Ed25519PublicKey

Security Notes:
    - NEVER commit private keys to source control
    - Store private keys only in GitHub Secrets or secure key vault
    - Rotate keys periodically for security
    - Use different keys for different environments (dev/staging/prod)
"""

import sys
import os
import base64
import hashlib


def sign_file_ed25519(file_path, private_key_base64):
    """
    Sign a file using Ed25519 signature algorithm.
    
    Args:
        file_path: Path to the file to sign
        private_key_base64: Base64-encoded Ed25519 private key (64 bytes)
    
    Returns:
        Base64-encoded signature string
    """
    try:
        # Import nacl library (Ed25519 implementation)
        from nacl.signing import SigningKey
        from nacl.encoding import Base64Encoder
        
        # Decode the private key from base64
        try:
            private_key_bytes = base64.b64decode(private_key_base64)
        except Exception as e:
            raise ValueError(f"Invalid base64 private key: {e}")
        
        if len(private_key_bytes) != 32:
            raise ValueError(f"Private key must be 32 bytes, got {len(private_key_bytes)} bytes")
        
        # Create signing key
        signing_key = SigningKey(private_key_bytes)
        
        # Read file content
        with open(file_path, 'rb') as f:
            file_content = f.read()
        
        # Sign the file content
        signed = signing_key.sign(file_content)
        
        # Extract just the signature (first 64 bytes)
        signature = signed.signature
        
        # Encode signature to base64
        signature_base64 = base64.b64encode(signature).decode('ascii')
        
        return signature_base64
        
    except ImportError:
        print("ERROR: PyNaCl library not found. Install it with: pip install PyNaCl", file=sys.stderr)
        sys.exit(1)
    except FileNotFoundError:
        print(f"ERROR: File not found: {file_path}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"ERROR: Failed to sign file: {e}", file=sys.stderr)
        sys.exit(1)


def main():
    """Main entry point for the script."""
    
    # Check command line arguments
    if len(sys.argv) < 2:
        print("Usage: python sign_zip.py <zip_file> [private_key_base64]", file=sys.stderr)
        print("", file=sys.stderr)
        print("Environment variable NETSPARKLE_PRIVATE_KEY can be used instead of command line argument.", file=sys.stderr)
        sys.exit(1)
    
    zip_file = sys.argv[1]
    
    # Get private key from argument or environment variable
    if len(sys.argv) >= 3:
        private_key_base64 = sys.argv[2]
    else:
        private_key_base64 = os.environ.get('NETSPARKLE_PRIVATE_KEY')
        if not private_key_base64:
            print("ERROR: Private key not provided. Use command line argument or NETSPARKLE_PRIVATE_KEY environment variable.", file=sys.stderr)
            sys.exit(1)
    
    # Verify file exists
    if not os.path.isfile(zip_file):
        print(f"ERROR: File not found: {zip_file}", file=sys.stderr)
        sys.exit(1)
    
    # Sign the file
    signature = sign_file_ed25519(zip_file, private_key_base64)
    
    # Output signature
    print(signature)
    
    # Also print file size for convenience (useful for appcast generation)
    file_size = os.path.getsize(zip_file)
    print(f"File size: {file_size} bytes", file=sys.stderr)


if __name__ == '__main__':
    main()
