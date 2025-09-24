#!/bin/bash
set -e

# ProdControlAV Agent Remote Deployment Script
# Orchestrates the complete deployment process from development environment to Raspberry Pi
# Usage: ./scripts/deploy-agent.sh --pi-host <host> --pi-user <user> --api-url <url> --api-key <key> [options]

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
PI_HOST=""
PI_USER="pi"
PI_SSH_PORT="22"
API_URL=""
API_KEY=""
SKIP_BUILD=false
SKIP_TESTS=false
BUILD_CONFIG="Release"
TEMP_DIR="/tmp/prodcontrol-deploy-$$"
DOCKER_BUILD=true
VERBOSE=false

# Function to print status messages
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_step() {
    echo -e "${BLUE}[STEP]${NC} $1"
}

print_verbose() {
    if [[ "$VERBOSE" == "true" ]]; then
        echo -e "${BLUE}[DEBUG]${NC} $1"
    fi
}

# Function to show usage
show_usage() {
    cat << EOF
ProdControlAV Agent Remote Deployment Script

USAGE:
    $0 --pi-host <host> --pi-user <user> --api-url <url> --api-key <key> [options]

REQUIRED PARAMETERS:
    --pi-host <host>        Raspberry Pi hostname or IP address
    --pi-user <user>        SSH username for Raspberry Pi (default: pi)
    --api-url <url>         API server base URL (e.g., https://your-server.com/api)
    --api-key <key>         Agent API key (minimum 32 characters)

OPTIONAL PARAMETERS:
    --pi-ssh-port <port>    SSH port for Raspberry Pi (default: 22)
    --skip-build           Skip building the agent (use existing published files)
    --skip-tests           Skip running tests before deployment
    --build-config <cfg>    Build configuration Release|Debug (default: Release)
    --no-docker            Use dotnet publish instead of Docker build
    --verbose              Enable verbose logging
    --help                 Show this help message

EXAMPLES:
    # Basic deployment
    $0 --pi-host 192.168.1.100 --pi-user pi --api-url https://myserver.com/api --api-key "your-secure-32-character-api-key-here"
    
    # With custom SSH port and verbose logging  
    $0 --pi-host raspberrypi.local --pi-user admin --pi-ssh-port 2222 --api-url https://myserver.com/api --api-key "your-api-key" --verbose

SECURITY:
    - Credentials are passed as command-line parameters (not stored in files)
    - SSH key-based authentication is recommended
    - API keys must be at least 32 characters for security
    - All network communication uses secure protocols

EOF
}

# Function to validate parameters
validate_parameters() {
    local errors=0
    
    if [[ -z "$PI_HOST" ]]; then
        print_error "Raspberry Pi host is required (--pi-host)"
        errors=$((errors + 1))
    fi
    
    if [[ -z "$PI_USER" ]]; then
        print_error "Raspberry Pi user is required (--pi-user)"
        errors=$((errors + 1))
    fi
    
    if [[ -z "$API_URL" ]]; then
        print_error "API URL is required (--api-url)"
        errors=$((errors + 1))
    fi
    
    if [[ -z "$API_KEY" ]]; then
        print_error "API key is required (--api-key)"
        errors=$((errors + 1))
    fi
    
    # Validate API URL format
    if [[ -n "$API_URL" ]] && [[ ! "$API_URL" =~ ^https?:// ]]; then
        print_error "API URL must start with http:// or https://"
        errors=$((errors + 1))
    fi
    
    # Validate API key length
    if [[ -n "$API_KEY" ]] && [[ ${#API_KEY} -lt 32 ]]; then
        print_error "API key must be at least 32 characters long for security"
        errors=$((errors + 1))
    fi
    
    # Validate SSH port
    if [[ ! "$PI_SSH_PORT" =~ ^[0-9]+$ ]] || [[ "$PI_SSH_PORT" -lt 1 ]] || [[ "$PI_SSH_PORT" -gt 65535 ]]; then
        print_error "SSH port must be a valid port number (1-65535)"
        errors=$((errors + 1))
    fi
    
    if [[ $errors -gt 0 ]]; then
        print_error "Please fix the above errors and try again"
        exit 1
    fi
}

# Function to check prerequisites
check_prerequisites() {
    print_step "Checking prerequisites..."
    
    # Check if we're in the right directory
    if [[ ! -f "ProdControlAV.sln" ]]; then
        print_error "This script must be run from the ProdControlAV repository root"
        exit 1
    fi
    
    # Check for required tools
    local missing_tools=()
    
    if ! command -v dotnet &> /dev/null; then
        missing_tools+=("dotnet")
    fi
    
    if [[ "$DOCKER_BUILD" == "true" ]] && ! command -v docker &> /dev/null; then
        missing_tools+=("docker")
    fi
    
    if ! command -v ssh &> /dev/null; then
        missing_tools+=("ssh")
    fi
    
    if ! command -v scp &> /dev/null; then
        missing_tools+=("scp")
    fi
    
    if [[ ${#missing_tools[@]} -gt 0 ]]; then
        print_error "Missing required tools: ${missing_tools[*]}"
        print_error "Please install the missing tools and try again"
        exit 1
    fi
    
    print_status "Prerequisites check passed"
}

# Function to test SSH connectivity
test_ssh_connection() {
    print_step "Testing SSH connection to Raspberry Pi..."
    
    if ssh -p "$PI_SSH_PORT" -o ConnectTimeout=10 -o BatchMode=yes "$PI_USER@$PI_HOST" "echo 'SSH connection successful'" 2>/dev/null; then
        print_status "SSH connection to $PI_USER@$PI_HOST:$PI_SSH_PORT successful"
    else
        print_error "Cannot connect to $PI_USER@$PI_HOST:$PI_SSH_PORT"
        print_error "Please ensure:"
        print_error "  - The Raspberry Pi is powered on and connected to the network"
        print_error "  - SSH is enabled on the Raspberry Pi"
        print_error "  - Your SSH key is added to the Pi's authorized_keys"
        print_error "  - The hostname/IP address and port are correct"
        exit 1
    fi
}

# Function to run tests
run_tests() {
    if [[ "$SKIP_TESTS" == "true" ]]; then
        print_warning "Skipping tests as requested"
        return
    fi
    
    print_step "Running tests..."
    
    if dotnet test --configuration "$BUILD_CONFIG" --logger "console;verbosity=minimal"; then
        print_status "All tests passed"
    else
        print_error "Tests failed - deployment aborted"
        exit 1
    fi
}

# Function to build the agent
build_agent() {
    if [[ "$SKIP_BUILD" == "true" ]]; then
        print_warning "Skipping build as requested"
        return
    fi
    
    print_step "Building ProdControlAV Agent..."
    
    # Create temporary directory for build output
    mkdir -p "$TEMP_DIR"
    
    if [[ "$DOCKER_BUILD" == "true" ]]; then
        print_status "Building with Docker for ARM64..."
        print_verbose "Docker build command: docker build -f src/ProdControlAV.Agent/Dockerfile --build-arg BUILD_CONFIGURATION=$BUILD_CONFIG -t prodcontrolav-agent:latest ."
        
        if docker build -f src/ProdControlAV.Agent/Dockerfile \
            --build-arg BUILD_CONFIGURATION="$BUILD_CONFIG" \
            -t prodcontrolav-agent:latest .; then
            print_status "Docker build completed successfully"
        else
            print_error "Docker build failed"
            cleanup
            exit 1
        fi
        
        # Extract built files from Docker image
        print_status "Extracting built files from Docker image..."
        container_id=$(docker create prodcontrolav-agent:latest)
        docker cp "$container_id:/app/" "$TEMP_DIR/publish"
        docker rm "$container_id"
        
    else
        print_status "Building with dotnet publish for ARM64..."
        print_verbose "Publish command: dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj -c $BUILD_CONFIG -r linux-arm64 --self-contained true -o $TEMP_DIR/publish"
        
        if dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj \
            -c "$BUILD_CONFIG" \
            -r linux-arm64 \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=true \
            -o "$TEMP_DIR/publish"; then
            print_status "dotnet publish completed successfully"
        else
            print_error "dotnet publish failed"
            cleanup
            exit 1
        fi
    fi
    
    # Verify the main binary exists
    if [[ ! -f "$TEMP_DIR/publish/ProdControlAV.Agent" ]]; then
        print_error "Build completed but ProdControlAV.Agent binary not found"
        cleanup
        exit 1
    fi
    
    print_status "Agent build completed successfully"
}

# Function to transfer files to Raspberry Pi
transfer_files() {
    print_step "Transferring files to Raspberry Pi..."
    
    # Create temporary directory on Pi
    print_status "Creating temporary directory on Raspberry Pi..."
    ssh -p "$PI_SSH_PORT" "$PI_USER@$PI_HOST" "mkdir -p /tmp/prodcontrol-agent-deploy"
    
    # Copy published files
    print_status "Copying published files to Raspberry Pi..."
    print_verbose "SCP command: scp -P $PI_SSH_PORT -r $TEMP_DIR/publish/* $PI_USER@$PI_HOST:/tmp/prodcontrol-agent-deploy/"
    
    if scp -P "$PI_SSH_PORT" -r "$TEMP_DIR/publish"/* "$PI_USER@$PI_HOST:/tmp/prodcontrol-agent-deploy/"; then
        print_status "File transfer completed successfully"
    else
        print_error "File transfer failed"
        cleanup
        exit 1
    fi
}

# Function to deploy and configure on Raspberry Pi
deploy_on_pi() {
    print_step "Deploying and configuring agent on Raspberry Pi..."
    
    # Create the remote deployment script
    local remote_script="/tmp/prodcontrol-remote-deploy.sh"
    
    cat > "$TEMP_DIR/remote-deploy.sh" << 'EOF'
#!/bin/bash
set -e

# Remote deployment script for Raspberry Pi
API_URL="$1"
API_KEY="$2"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

print_status() {
    echo -e "${GREEN}[PI-INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[PI-WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[PI-ERROR]${NC} $1"
}

print_status "Starting deployment on Raspberry Pi..."

# Check if published files exist
if [[ ! -f "/tmp/prodcontrol-agent-deploy/ProdControlAV.Agent" ]]; then
    print_error "ProdControlAV.Agent binary not found in /tmp/prodcontrol-agent-deploy/"
    exit 1
fi

# Stop existing service if running
if systemctl is-active --quiet prodcontrolav-agent 2>/dev/null; then
    print_status "Stopping existing agent service..."
    sudo systemctl stop prodcontrolav-agent
fi

# Create application directory
print_status "Creating application directory..."
sudo mkdir -p /opt/prodcontrolav/agent

# Backup existing installation if it exists
if [[ -d "/opt/prodcontrolav/agent" ]] && [[ -f "/opt/prodcontrolav/agent/ProdControlAV.Agent" ]]; then
    print_status "Backing up existing installation..."
    sudo cp -r /opt/prodcontrolav/agent "/opt/prodcontrolav/agent.backup.$(date +%Y%m%d-%H%M%S)"
fi

# Copy new files
print_status "Installing new agent files..."
sudo cp -r /tmp/prodcontrol-agent-deploy/* /opt/prodcontrolav/agent/

# Create prodctl user if it doesn't exist
if ! id "prodctl" &>/dev/null; then
    print_status "Creating prodctl user..."
    sudo useradd -r -s /usr/sbin/nologin -d /opt/prodcontrolav prodctl
else
    print_status "User 'prodctl' already exists"
fi

# Set ownership
print_status "Setting file ownership..."
sudo chown -R prodctl:prodctl /opt/prodcontrolav

# Install capabilities tools if needed
if ! command -v setcap &> /dev/null; then
    print_status "Installing libcap2-bin..."
    sudo apt-get update -qq
    sudo apt-get install -y libcap2-bin
fi

# Set ICMP capability
print_status "Setting ICMP capability on agent binary..."
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent

# Verify capability was set
if getcap /opt/prodcontrolav/agent/ProdControlAV.Agent | grep -q "cap_net_raw+ep"; then
    print_status "ICMP capability set successfully"
else
    print_error "Failed to set ICMP capability"
    exit 1
fi

# Create environment configuration
print_status "Creating environment configuration..."
sudo tee /opt/prodcontrolav/agent/.env > /dev/null << ENVEOF
# ProdControlAV Agent Configuration
# This file contains sensitive information - keep it secure
PRODCONTROL_API_URL=${API_URL}
PRODCONTROL_AGENT_APIKEY=${API_KEY}
ENVEOF

# Secure the environment file
sudo chown prodctl:prodctl /opt/prodcontrolav/agent/.env
sudo chmod 600 /opt/prodcontrolav/agent/.env

# Install systemd service
if [[ -f "/opt/prodcontrolav/agent/scripts/prodcontrolav-agent.service" ]]; then
    print_status "Installing systemd service..."
    sudo cp /opt/prodcontrolav/agent/scripts/prodcontrolav-agent.service /etc/systemd/system/
    sudo systemctl daemon-reload
    sudo systemctl enable prodcontrolav-agent
    print_status "Systemd service installed and enabled"
else
    print_error "Systemd service file not found in scripts directory"
    exit 1
fi

print_status "Deployment completed successfully"
EOF

    # Copy the remote script to Pi and execute it
    print_status "Copying deployment script to Raspberry Pi..."
    scp -P "$PI_SSH_PORT" "$TEMP_DIR/remote-deploy.sh" "$PI_USER@$PI_HOST:$remote_script"
    
    print_status "Executing deployment on Raspberry Pi..."
    ssh -p "$PI_SSH_PORT" "$PI_USER@$PI_HOST" "chmod +x $remote_script && $remote_script '$API_URL' '$API_KEY'"
    
    print_status "Cleaning up temporary files on Raspberry Pi..."
    ssh -p "$PI_SSH_PORT" "$PI_USER@$PI_HOST" "rm -rf /tmp/prodcontrol-agent-deploy $remote_script"
}

# Function to start the agent service
start_agent_service() {
    print_step "Starting ProdControlAV Agent service..."
    
    if ssh -p "$PI_SSH_PORT" "$PI_USER@$PI_HOST" "sudo systemctl start prodcontrolav-agent"; then
        print_status "Agent service started successfully"
    else
        print_error "Failed to start agent service"
        print_error "Check the service status with: sudo systemctl status prodcontrolav-agent"
        exit 1
    fi
    
    # Wait a moment for the service to initialize
    sleep 3
    
    # Check service status
    print_status "Checking agent service status..."
    if ssh -p "$PI_SSH_PORT" "$PI_USER@$PI_HOST" "sudo systemctl is-active --quiet prodcontrolav-agent"; then
        print_status "Agent service is running successfully"
    else
        print_warning "Agent service may have failed to start properly"
        print_warning "Check the service status and logs on the Raspberry Pi:"
        print_warning "  sudo systemctl status prodcontrolav-agent"
        print_warning "  sudo journalctl -u prodcontrolav-agent -f"
    fi
}

# Function to show deployment summary
show_summary() {
    print_step "Deployment Summary"
    echo
    echo -e "${GREEN}✓ Agent successfully deployed to: $PI_USER@$PI_HOST${NC}"
    echo -e "${GREEN}✓ Service installed and started${NC}"
    echo -e "${GREEN}✓ Configuration applied${NC}"
    echo
    echo "Service Management Commands (run on Raspberry Pi):"
    echo "  Check status:   sudo systemctl status prodcontrolav-agent"
    echo "  View logs:      sudo journalctl -u prodcontrolav-agent -f"
    echo "  Stop service:   sudo systemctl stop prodcontrolav-agent"
    echo "  Start service:  sudo systemctl start prodcontrolav-agent"
    echo "  Restart:        sudo systemctl restart prodcontrolav-agent"
    echo
    echo "Configuration files on Raspberry Pi:"
    echo "  Agent binary:   /opt/prodcontrolav/agent/ProdControlAV.Agent"
    echo "  Environment:    /opt/prodcontrolav/agent/.env"
    echo "  Service file:   /etc/systemd/system/prodcontrolav-agent.service"
    echo
}

# Cleanup function
cleanup() {
    if [[ -d "$TEMP_DIR" ]]; then
        print_status "Cleaning up temporary files..."
        rm -rf "$TEMP_DIR"
    fi
}

# Trap to ensure cleanup on exit
trap cleanup EXIT

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --pi-host)
            PI_HOST="$2"
            shift 2
            ;;
        --pi-user)
            PI_USER="$2"
            shift 2
            ;;
        --pi-ssh-port)
            PI_SSH_PORT="$2"
            shift 2
            ;;
        --api-url)
            API_URL="$2"
            shift 2
            ;;
        --api-key)
            API_KEY="$2"
            shift 2
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --build-config)
            BUILD_CONFIG="$2"
            shift 2
            ;;
        --no-docker)
            DOCKER_BUILD=false
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --help)
            show_usage
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo
            show_usage
            exit 1
            ;;
    esac
done

# Main execution
main() {
    echo -e "${GREEN}ProdControlAV Agent Remote Deployment${NC}"
    echo "====================================="
    echo
    
    # Validate parameters
    validate_parameters
    
    # Check prerequisites
    check_prerequisites
    
    # Test SSH connection
    test_ssh_connection
    
    # Run tests
    run_tests
    
    # Build the agent
    build_agent
    
    # Transfer files
    transfer_files
    
    # Deploy on Pi
    deploy_on_pi
    
    # Start the service
    start_agent_service
    
    # Show summary
    show_summary
    
    print_status "Deployment completed successfully!"
}

# Execute main function
main "$@"