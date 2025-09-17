#!/bin/bash
set -e

# ProdControlAV Agent Deployment Script for Raspberry Pi 5
# Run this script on the Raspberry Pi after copying the published files

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}ProdControlAV Agent Deployment Script${NC}"
echo "=================================="

# Check if running as root
if [[ $EUID -eq 0 ]]; then
   echo -e "${RED}This script should not be run as root${NC}"
   echo "Please run as a regular user with sudo access"
   exit 1
fi

# Function to print status
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're on ARM64
ARCH=$(uname -m)
if [[ "$ARCH" != "aarch64" ]]; then
    print_warning "This script is designed for ARM64 (aarch64) architecture"
    print_warning "Current architecture: $ARCH"
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        exit 1
    fi
fi

# Check if published files exist
if [[ ! -f "./ProdControlAV.Agent" ]]; then
    print_error "ProdControlAV.Agent binary not found in current directory"
    print_error "Please run this script from the directory containing the published files"
    exit 1
fi

print_status "Creating application directory..."
sudo mkdir -p /opt/prodcontrolav/agent

print_status "Copying application files..."
sudo cp -r ./* /opt/prodcontrolav/agent/

print_status "Creating prodctl user..."
if ! id "prodctl" &>/dev/null; then
    sudo useradd -r -s /usr/sbin/nologin -d /opt/prodcontrolav prodctl
    print_status "User 'prodctl' created"
else
    print_status "User 'prodctl' already exists"
fi

print_status "Setting file ownership..."
sudo chown -R prodctl:prodctl /opt/prodcontrolav

print_status "Installing capabilities tools..."
sudo apt-get update -qq
sudo apt-get install -y libcap2-bin

print_status "Setting ICMP capability on agent binary..."
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent

# Verify capability was set
if getcap /opt/prodcontrolav/agent/ProdControlAV.Agent | grep -q "cap_net_raw+ep"; then
    print_status "ICMP capability set successfully"
else
    print_error "Failed to set ICMP capability"
    exit 1
fi

# Prompt for configuration
echo
echo "=================================="
print_status "Configuration Setup"
echo "=================================="
echo
print_warning "You need to configure the API URL and API key for the agent"
echo "The API key must be at least 32 characters long"
echo

# Check if .env already exists
if [[ -f "/opt/prodcontrolav/agent/.env" ]]; then
    print_warning "Environment file already exists"
    read -p "Do you want to update the configuration? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Skipping configuration update"
    else
        UPDATE_CONFIG=true
    fi
else
    UPDATE_CONFIG=true
fi

if [[ "$UPDATE_CONFIG" == "true" ]]; then
    # Prompt for API URL
    while true; do
        read -p "Enter API Base URL (e.g., https://your-api-server.com/api): " API_URL
        if [[ -z "$API_URL" ]]; then
            print_error "API URL cannot be empty"
            continue
        fi
        if [[ ! "$API_URL" =~ ^https?:// ]]; then
            print_error "API URL must start with http:// or https://"
            continue
        fi
        break
    done
    
    # Prompt for API key
    while true; do
        read -s -p "Enter API key (32+ characters): " API_KEY
        echo
        if [[ ${#API_KEY} -lt 32 ]]; then
            print_error "API key must be at least 32 characters long"
            continue
        fi
        break
    done

    print_status "Creating environment configuration..."
    sudo tee /opt/prodcontrolav/agent/.env > /dev/null << EOF
# ProdControlAV Agent Configuration
# This file contains sensitive information - keep it secure
PRODCONTROL_API_URL=${API_URL}
PRODCONTROL_AGENT_APIKEY=${API_KEY}
EOF

    print_status "Securing environment file..."
    sudo chown prodctl:prodctl /opt/prodcontrolav/agent/.env
    sudo chmod 600 /opt/prodcontrolav/agent/.env
fi

# Install systemd service
print_status "Installing systemd service..."
if [[ -f "/opt/prodcontrolav/agent/scripts/prodcontrolav-agent.service" ]]; then
    sudo cp /opt/prodcontrolav/agent/scripts/prodcontrolav-agent.service /etc/systemd/system/
    print_status "Systemd service file installed"
else
    print_error "Systemd service file not found in scripts directory"
    exit 1
fi

print_status "Reloading systemd configuration..."
sudo systemctl daemon-reload

print_status "Enabling service for automatic startup..."
sudo systemctl enable prodcontrolav-agent

echo
echo "=================================="
print_status "Deployment Complete!"
echo "=================================="
echo
echo "Next steps:"
echo "1. Edit /opt/prodcontrolav/agent/appsettings.json to configure API endpoints"
echo "2. Start the service: sudo systemctl start prodcontrolav-agent"
echo "3. Check status: sudo systemctl status prodcontrolav-agent"
echo "4. View logs: sudo journalctl -u prodcontrolav-agent -f"
echo
print_warning "Remember to configure your firewall and network settings as needed"
print_warning "The service is enabled but not started - start it manually when ready"
echo