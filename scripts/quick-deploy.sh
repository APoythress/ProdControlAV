#!/bin/bash

# ProdControlAV Agent Quick Deploy
# A simplified interface for deploying the agent with prompts for required information

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${GREEN}ProdControlAV Agent Quick Deploy${NC}"
echo "================================"
echo
echo "This script will guide you through deploying the ProdControlAV Agent to your Raspberry Pi."
echo

# Function to read input with validation
read_with_validation() {
    local prompt="$1"
    local validate_func="$2"
    local value=""
    
    while true; do
        read -p "$prompt: " value
        if [[ -z "$value" ]]; then
            echo "  ❌ This field is required"
            continue
        fi
        
        if [[ -n "$validate_func" ]] && ! $validate_func "$value"; then
            continue
        fi
        
        echo "$value"
        return 0
    done
}

# Function to read sensitive input
read_sensitive() {
    local prompt="$1"
    local validate_func="$2"
    local value=""
    
    while true; do
        read -s -p "$prompt: " value
        echo  # New line after hidden input
        if [[ -z "$value" ]]; then
            echo "  ❌ This field is required"
            continue
        fi
        
        if [[ -n "$validate_func" ]] && ! $validate_func "$value"; then
            continue
        fi
        
        echo "$value"
        return 0
    done
}

# Validation functions
validate_url() {
    local url="$1"
    if [[ ! "$url" =~ ^https?:// ]]; then
        echo "  ❌ URL must start with http:// or https://"
        return 1
    fi
    return 0
}

validate_api_key() {
    local key="$1"
    if [[ ${#key} -lt 32 ]]; then
        echo "  ❌ API key must be at least 32 characters long"
        return 1
    fi
    return 0
}

validate_port() {
    local port="$1"
    if [[ ! "$port" =~ ^[0-9]+$ ]] || [[ "$port" -lt 1 ]] || [[ "$port" -gt 65535 ]]; then
        echo "  ❌ Port must be a number between 1 and 65535"
        return 1
    fi
    return 0
}

# Collect required information
echo -e "${BLUE}🔧 Raspberry Pi Connection${NC}"
PI_HOST=$(read_with_validation "Raspberry Pi hostname or IP address (e.g., 192.168.1.100)")
PI_USER=$(read_with_validation "SSH username" "")
echo

# Optional SSH port
read -p "SSH port (press Enter for default 22): " PI_SSH_PORT
if [[ -z "$PI_SSH_PORT" ]]; then
    PI_SSH_PORT="22"
else
    while ! validate_port "$PI_SSH_PORT"; do
        read -p "SSH port (1-65535): " PI_SSH_PORT
    done
fi
echo

echo -e "${BLUE}🌐 API Configuration${NC}"
API_URL=$(read_with_validation "API server URL (e.g., https://your-server.com/api)" "validate_url")
API_KEY=$(read_sensitive "API key (32+ characters)" "validate_api_key")
echo

# Optional build settings
echo -e "${BLUE}⚙️ Build Options${NC}"
echo "Press Enter to use defaults, or specify custom options:"

read -p "Build configuration (Release): " BUILD_CONFIG
if [[ -z "$BUILD_CONFIG" ]]; then
    BUILD_CONFIG="Release"
fi

read -p "Skip tests? (y/N): " SKIP_TESTS
if [[ "$SKIP_TESTS" =~ ^[Yy]$ ]]; then
    SKIP_TESTS_FLAG="--skip-tests"
else
    SKIP_TESTS_FLAG=""
fi

read -p "Skip build (use existing)? (y/N): " SKIP_BUILD
if [[ "$SKIP_BUILD" =~ ^[Yy]$ ]]; then
    SKIP_BUILD_FLAG="--skip-build"
else
    SKIP_BUILD_FLAG=""
fi

read -p "Use dotnet publish instead of Docker? (y/N): " NO_DOCKER
if [[ "$NO_DOCKER" =~ ^[Yy]$ ]]; then
    NO_DOCKER_FLAG="--no-docker"
else
    NO_DOCKER_FLAG=""
fi

read -p "Enable verbose logging? (y/N): " VERBOSE
if [[ "$VERBOSE" =~ ^[Yy]$ ]]; then
    VERBOSE_FLAG="--verbose"
else
    VERBOSE_FLAG=""
fi

echo
echo -e "${BLUE}📋 Deployment Summary${NC}"
echo "Raspberry Pi: $PI_USER@$PI_HOST:$PI_SSH_PORT"
echo "API URL: $API_URL"
echo "API Key: [HIDDEN]"
echo "Build Config: $BUILD_CONFIG"
echo

read -p "Proceed with deployment? (Y/n): " CONFIRM
if [[ "$CONFIRM" =~ ^[Nn]$ ]]; then
    echo "Deployment cancelled."
    exit 0
fi

echo
echo -e "${GREEN}🚀 Starting deployment...${NC}"
echo

# Build the command
DEPLOY_CMD="./scripts/deploy-agent.sh"
DEPLOY_CMD="$DEPLOY_CMD --pi-host '$PI_HOST'"
DEPLOY_CMD="$DEPLOY_CMD --pi-user '$PI_USER'"
DEPLOY_CMD="$DEPLOY_CMD --pi-ssh-port '$PI_SSH_PORT'"
DEPLOY_CMD="$DEPLOY_CMD --api-url '$API_URL'"
DEPLOY_CMD="$DEPLOY_CMD --api-key '$API_KEY'"
DEPLOY_CMD="$DEPLOY_CMD --build-config '$BUILD_CONFIG'"

if [[ -n "$SKIP_TESTS_FLAG" ]]; then
    DEPLOY_CMD="$DEPLOY_CMD $SKIP_TESTS_FLAG"
fi

if [[ -n "$SKIP_BUILD_FLAG" ]]; then
    DEPLOY_CMD="$DEPLOY_CMD $SKIP_BUILD_FLAG"
fi

if [[ -n "$NO_DOCKER_FLAG" ]]; then
    DEPLOY_CMD="$DEPLOY_CMD $NO_DOCKER_FLAG"
fi

if [[ -n "$VERBOSE_FLAG" ]]; then
    DEPLOY_CMD="$DEPLOY_CMD $VERBOSE_FLAG"
fi

# Execute the deployment
eval "$DEPLOY_CMD"