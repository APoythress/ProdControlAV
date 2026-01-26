#!/usr/bin/env python3
"""
Generate NetSparkle appcast.json manifest from template.

This script generates an appcast.json manifest file for NetSparkle automatic updates.
It reads a template, fills in the necessary information (version, URL, signature, size),
and outputs the final manifest.

Usage:
    python make_appcast.py --template <template_file> --version <version> \
        --url <download_url> --signature <ed25519_signature> --size <file_size> \
        --output <output_file> [--description <description>]

Arguments:
    --template: Path to appcast.template.json file
    --version: Version number (e.g., "0.2.0")
    --url: URL to the release ZIP file in Azure Blob Storage
    --signature: Base64-encoded Ed25519 signature
    --size: Size of ZIP file in bytes
    --output: Path to output appcast.json file
    --description: Optional description of the release (default: "Release version {version}")
    --pub-date: Optional publication date in ISO format (default: current UTC time)
    --critical: Optional flag to mark update as critical (default: false)

Environment Variables:
    RELEASE_VERSION: Version number (alternative to --version)
    RELEASE_URL: Download URL (alternative to --url)
    RELEASE_SIGNATURE: Ed25519 signature (alternative to --signature)
    RELEASE_SIZE: File size in bytes (alternative to --size)

Example:
    python make_appcast.py \
        --template appcast.template.json \
        --version "0.2.1" \
        --url "https://storage.blob.core.windows.net/updates/agent-0.2.1.zip" \
        --signature "BASE64_SIGNATURE_HERE" \
        --size 12345678 \
        --output appcast.json \
        --description "Bug fixes and performance improvements"

Notes:
    - The template should have a placeholder item that will be updated with new information
    - Multiple versions can be maintained in the manifest for rollback support
    - The script preserves existing items in the manifest if output file already exists
"""

import sys
import os
import json
import argparse
from datetime import datetime, timezone


def load_template(template_path):
    """Load the appcast template from file."""
    try:
        with open(template_path, 'r') as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"ERROR: Template file not found: {template_path}", file=sys.stderr)
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"ERROR: Invalid JSON in template: {e}", file=sys.stderr)
        sys.exit(1)


def create_appcast_item(version, url, signature, size, description, pubDate, critical):
    """Create a new appcast item with the provided information."""
    return {
        "title": f"Version {version}",
        "version": version,
        "shortVersion": version,
        "pubDate": pubDate,
        "url": url,
        "description": description,
        "size": int(size),
        "type": "application/zip",
        "signature": signature,  # NetSparkle expects signature as a string, not an object
        "os": "linux",
        "criticalUpdate": critical,
        "minSystemVersion": "0.0.0"
    }


def update_appcast(template, newItem, keepHistory=True, maxHistory=10):
    """
    Update the appcast with a new item.
    
    Args:
        template: The appcast template/existing manifest
        newItem: The new release item to add
        keepHistory: Whether to keep old versions in the manifest
        maxHistory: Maximum number of historical versions to keep
    
    Returns:
        Updated appcast dictionary
    """
    appcast = template.copy()
    
    if keepHistory:
        # Add new item to the beginning of the list
        existingItems = appcast.get("items", [])
        
        # Filter out any existing item with the same version
        existingItems = [item for item in existingItems if item.get("version") != newItem["version"]]
        
        # Add new item at the beginning
        items = [newItem] + existingItems
        
        # Keep only the most recent items
        items = items[:max_history]
        
        appcast["items"] = items
    else:
        # Replace all items with just the new one
        appcast["items"] = [newItem]
    
    return appcast


def save_appcast(appcast, outputPath):
    """Save the appcast to a file."""
    try:
        with open(outputPath, 'w') as f:
            json.dump(appcast, f, indent=2)
        print(f"Appcast saved to: {outputPath}", file=sys.stderr)
    except Exception as e:
        print(f"ERROR: Failed to save appcast: {e}", file=sys.stderr)
        sys.exit(1)


def main():
    """Main entry point for the script."""
    
    parser = argparse.ArgumentParser(
        description="Generate NetSparkle appcast.json manifest from template"
    )
    parser.add_argument("--template", required=True, help="Path to appcast.template.json")
    parser.add_argument("--version", help="Version number (e.g., '0.2.0')")
    parser.add_argument("--url", help="URL to the release ZIP file")
    parser.add_argument("--signature", help="Base64-encoded Ed25519 signature")
    parser.add_argument("--size", help="Size of ZIP file in bytes")
    parser.add_argument("--output", required=True, help="Path to output appcast.json")
    parser.add_argument("--description", help="Release description")
    parser.add_argument("--pubDate", help="Publication date in ISO format")
    parser.add_argument("--critical", action="store_true", help="Mark as critical update")
    parser.add_argument("--noHistory", action="store_true", help="Don't keep version history")
    parser.add_argument("--maxHistory", type=int, default=10, help="Maximum versions to keep (default: 10)")
    
    args = parser.parse_args()
    
    # Get values from args or environment variables
    version = args.version or os.environ.get('RELEASE_VERSION')
    url = args.url or os.environ.get('RELEASE_URL')
    signature = args.signature or os.environ.get('RELEASE_SIGNATURE')
    size = args.size or os.environ.get('RELEASE_SIZE')
    
    # Validate required parameters
    if not version:
        print("ERROR: Version is required (--version or RELEASE_VERSION)", file=sys.stderr)
        sys.exit(1)
    if not url:
        print("ERROR: URL is required (--url or RELEASE_URL)", file=sys.stderr)
        sys.exit(1)
    if not signature:
        print("ERROR: Signature is required (--signature or RELEASE_SIGNATURE)", file=sys.stderr)
        sys.exit(1)
    if not size:
        print("ERROR: Size is required (--size or RELEASE_SIZE)", file=sys.stderr)
        sys.exit(1)
    
    # Set defaults
    description = args.description or f"Release version {version}"
    pubDate = args.pubDate or datetime.now(timezone.utc).isoformat()
    
    # Load template
    template = load_template(args.template)
    
    # Create new item
    newItem = create_appcast_item(
        version=version,
        url=url,
        signature=signature,
        size=size,
        description=description,
        pubDate=pubDate,
        critical=args.critical
    )
    
    # Update appcast
    appcast = update_appcast(
        template=template,
        newItem=newItem,
        keepHistory=not args.noHistory,
        maxHistory=args.maxHistory
    )
    
    # Save to file
    save_appcast(appcast, args.output)
    
    print(f"Successfully generated appcast for version {version}", file=sys.stderr)


if __name__ == '__main__':
    main()
