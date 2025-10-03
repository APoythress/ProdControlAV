# Monitoring and Troubleshooting Guide

Comprehensive guide for monitoring ProdControlAV.Agent performance and troubleshooting common issues.

## Agent Health Monitoring

### System Resource Monitoring

#### Memory Usage
Monitor agent memory consumption:

```bash
# Check current memory usage
ps aux | grep ProdControlAV.Agent | awk '{print $4, $6}'

# Monitor memory over time
while true; do
    echo "$(date): $(ps -o pid,ppid,cmd,%mem,%cpu --sort=-%mem -C ProdControlAV.Agent)"
    sleep 60
done
```

#### CPU Usage
Track CPU utilization:

```bash
# Real-time CPU monitoring
top -p $(pgrep ProdControlAV.Agent)

# CPU usage over time
iostat -c 1 10
```

#### Network Usage
Monitor network traffic:

```bash
# Network connections
sudo netstat -tulpn | grep ProdControlAV.Agent

# Network bandwidth usage
sudo iftop -i eth0
```

### Application Monitoring

#### Log Analysis
Monitor application logs for health indicators:

```bash
# Monitor logs in real-time
sudo journalctl -u prodcontrolav-agent -f

# Filter for specific log levels
sudo journalctl -u prodcontrolav-agent -p err

# Search for specific events
sudo journalctl -u prodcontrolav-agent | grep -i "heartbeat\|error\|warning"

# Monitor device ping activity
sudo journalctl -u prodcontrolav-agent | grep -i "ping cycle\|ping result"

# Monitor status publishing
sudo journalctl -u prodcontrolav-agent | grep -i "publishing status\|state change posted"

# Monitor device list refresh
sudo journalctl -u prodcontrolav-agent | grep -i "device list refreshed"

# Track device state changes
sudo journalctl -u prodcontrolav-agent | grep -i "device state changed"
```

#### Key Health Indicators
Monitor these log patterns:

| Pattern | Meaning | Action |
|---------|---------|---------|
| `Heartbeat sent successfully` | Agent communicating with API | Normal operation |
| `Device monitoring cycle completed` | Monitoring working | Normal operation |
| `Starting device ping cycle for X devices` | Ping cycle initiated | Normal operation |
| `Ping result for {Name} ({Ip}): UP/DOWN` | Device ping status | Monitor for patterns |
| `Device state changed to ONLINE/OFFLINE` | Device status changed | Review device health |
| `Publishing status update for device` | Status being sent to API | Normal operation |
| `State change posted successfully` | API received status update | Normal operation |
| `Device list refreshed successfully` | Devices loaded from API | Normal operation |
| `API communication failed` | Network/API issues | Check connectivity |
| `Device ping timeout` | Device unreachable | Check device status |
| `Failed to publish status` | Status update failed | Check API connectivity |
| `Memory pressure detected` | High memory usage | Monitor resources |

#### Performance Metrics
Track key performance indicators:

```bash
# Device monitoring response times
sudo journalctl -u prodcontrolav-agent | grep "ping response" | tail -20

# API communication latency
sudo journalctl -u prodcontrolav-agent | grep "API call duration" | tail -20

# Error rates
sudo journalctl -u prodcontrolav-agent --since "1 hour ago" | grep -c ERROR
```

## Automated Monitoring Setup

### Systemd Monitoring
Configure systemd to restart the service on failure:

```bash
# Edit service file to add monitoring
sudo systemctl edit prodcontrolav-agent

# Add override configuration
[Service]
# Restart on any exit code except clean exit
Restart=on-failure
RestartSec=10

# Start with limited resources initially
StartLimitBurst=5
StartLimitIntervalSec=300

# Send watchdog notifications
WatchdogSec=60
```

### Log Rotation
Configure log rotation to prevent disk space issues:

```bash
# Create log rotation config
sudo tee /etc/logrotate.d/prodcontrolav-agent << 'EOF'
/var/log/prodcontrolav-agent.log {
    daily
    rotate 30
    compress
    delaycompress
    missingok
    create 640 syslog adm
    postrotate
        systemctl reload-or-restart rsyslog
    endscript
}
EOF
```

### Health Check Script
Create automated health monitoring:

```bash
# Create health check script
sudo tee /usr/local/bin/check-agent-health.sh << 'EOF'
#!/bin/bash

SERVICE_NAME="prodcontrolav-agent"
API_URL="https://your-server.com/api"
ALERT_EMAIL="admin@yourcompany.com"

# Check if service is running
if ! systemctl is-active --quiet $SERVICE_NAME; then
    echo "ALERT: $SERVICE_NAME is not running" | mail -s "Agent Down Alert" $ALERT_EMAIL
    systemctl restart $SERVICE_NAME
fi

# Check API connectivity
if ! curl -s --max-time 10 $API_URL/health > /dev/null; then
    echo "ALERT: Cannot reach API server at $API_URL" | mail -s "API Connectivity Alert" $ALERT_EMAIL
fi

# Check memory usage
MEM_USAGE=$(ps -o pid,ppid,cmd,%mem --sort=-%mem -C ProdControlAV.Agent | awk 'NR==2{print $4}')
if (( $(echo "$MEM_USAGE > 80" | bc -l) )); then
    echo "ALERT: Agent memory usage is ${MEM_USAGE}%" | mail -s "High Memory Alert" $ALERT_EMAIL
fi
EOF

sudo chmod +x /usr/local/bin/check-agent-health.sh

# Add to crontab for regular checking
(crontab -l 2>/dev/null; echo "*/5 * * * * /usr/local/bin/check-agent-health.sh") | crontab -
```

## Common Issues and Solutions

### 1. Service Startup Issues

#### Symptoms
- Service fails to start
- Immediate service exit
- Permission errors in logs

#### Diagnostic Steps
```bash
# Check service status
sudo systemctl status prodcontrolav-agent

# View recent logs
sudo journalctl -u prodcontrolav-agent --since "10 minutes ago"

# Check file permissions
ls -la /opt/prodcontrolav/agent/
ls -la /opt/prodcontrolav/agent/.env

# Verify user exists
id prodctl
```

#### Solutions
```bash
# Fix file ownership
sudo chown -R prodctl:prodctl /opt/prodcontrolav/agent/

# Fix execute permissions
sudo chmod +x /opt/prodcontrolav/agent/ProdControlAV.Agent

# Fix environment file permissions
sudo chmod 600 /opt/prodcontrolav/agent/.env

# Reset service
sudo systemctl daemon-reload
sudo systemctl restart prodcontrolav-agent
```

### 2. API Communication Failures

#### Symptoms
- "API communication failed" in logs
- No heartbeat messages
- Device status not updating

#### Diagnostic Steps
```bash
# Test API connectivity manually
curl -v https://your-server.com/api/health

# Check DNS resolution
nslookup your-server.com

# Test with agent's API key
curl -H "X-API-Key: your-api-key" https://your-server.com/api/agents

# Check firewall rules
sudo ufw status
```

#### Solutions
```bash
# Fix DNS issues
echo "nameserver 8.8.8.8" | sudo tee -a /etc/resolv.conf

# Update API URL in configuration
sudo nano /opt/prodcontrolav/agent/.env
# Update PRODCONTROL_API_URL

# Regenerate API key if expired
# Contact API administrator for new key

# Restart service after configuration changes
sudo systemctl restart prodcontrolav-agent
```

### 3. Device Monitoring Issues

#### Symptoms
- Devices showing offline when they're online
- Ping timeouts in logs
- No device status updates
- Status updates not appearing in API logs

#### Diagnostic Steps
```bash
# Test ping manually
ping -c 4 192.168.1.100

# Check network capabilities
sudo getcap /opt/prodcontrolav/agent/ProdControlAV.Agent

# Test from agent user context
sudo -u prodctl ping -c 1 192.168.1.100

# Check routing
ip route show

# Monitor agent logs for ping activity
sudo journalctl -u prodcontrolav-agent -f | grep -i "ping cycle\|ping result"

# Check if device list is being loaded
sudo journalctl -u prodcontrolav-agent | grep -i "device list refreshed"

# Verify status publishing
sudo journalctl -u prodcontrolav-agent | grep -i "publishing status\|state change posted"

# Check API token status
sudo journalctl -u prodcontrolav-agent | grep -i "jwt token\|authentication"
```

#### Solutions
```bash
# Add network capabilities
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent

# Fix network routing if needed
sudo ip route add 192.168.1.0/24 via 192.168.1.1

# Restart networking
sudo systemctl restart networking

# Restart agent
sudo systemctl restart prodcontrolav-agent
```

### 4. High Memory Usage

#### Symptoms
- System becoming slow
- Out of memory errors
- High swap usage

#### Diagnostic Steps
```bash
# Check memory usage
free -h
ps aux --sort=-%mem | head -10

# Check for memory leaks
sudo journalctl -u prodcontrolav-agent | grep -i memory

# Monitor memory over time
while true; do
    echo "$(date): $(free -m | grep Mem)"
    sleep 300
done
```

#### Solutions
```bash
# Add swap space if needed
sudo dd if=/dev/zero of=/swapfile bs=1M count=1024
sudo mkswap /swapfile
sudo swapon /swapfile

# Reduce monitoring frequency
sudo nano /opt/prodcontrolav/agent/appsettings.json
# Increase IntervalSeconds value

# Restart service
sudo systemctl restart prodcontrolav-agent

# Consider upgrading hardware if persistent
```

### 5. Network Performance Issues

#### Symptoms
- Slow ping responses
- Timeout errors
- Network connectivity drops

#### Diagnostic Steps
```bash
# Test network latency
ping -c 10 8.8.8.8

# Check network interface
sudo ethtool eth0

# Monitor network errors
cat /proc/net/dev

# Check bandwidth usage
sudo iftop -i eth0
```

#### Solutions
```bash
# Reset network interface
sudo ip link set eth0 down
sudo ip link set eth0 up

# Check/replace network cable
# Update network drivers if needed

# Adjust network buffer sizes
echo 'net.core.rmem_max = 134217728' | sudo tee -a /etc/sysctl.conf
echo 'net.core.wmem_max = 134217728' | sudo tee -a /etc/sysctl.conf
sudo sysctl -p
```

## Performance Tuning

### Optimizing Monitoring Intervals

#### Configuration Tuning
Adjust monitoring frequency based on needs:

```json
{
    "Agent": {
        "IntervalSeconds": 30,          // Reduce from 15 for less CPU usage
        "DeviceTimeout": 5000,          // Adjust timeout for slower devices
        "MaxConcurrentPings": 20,       // Limit concurrent operations
        "BackoffStrategy": "exponential" // Implement backoff for failed pings
    }
}
```

#### Network-Specific Optimizations
```json
{
    "NetworkMonitoring": {
        "PingPacketSize": 56,           // Smaller packets for slower networks
        "PingTimeout": 3000,            // Longer timeout for remote devices
        "RetryAttempts": 2,             // Reduce retries to save bandwidth
        "AdaptiveInterval": true        // Adjust interval based on network conditions
    }
}
```

### Resource Limits

#### Systemd Resource Limits
Limit resource usage:

```bash
sudo systemctl edit prodcontrolav-agent

# Add resource limits
[Service]
MemoryMax=500M
CPUQuota=50%
TasksMax=100
```

#### Process Limits
Set process-level limits:

```bash
# Edit service file
sudo nano /etc/systemd/system/prodcontrolav-agent.service

# Add limits
[Service]
LimitNOFILE=1024
LimitNPROC=64
LimitCORE=0
```

## Log Management

### Log Levels Configuration
Configure appropriate log levels:

```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "ProdControlAV": "Debug",        // Detailed agent logs
            "Microsoft": "Warning",         // Reduce framework noise
            "System.Net.Http": "Warning"    // Reduce HTTP client logs
        }
    }
}
```

### Structured Logging
Enable structured logging for better analysis:

```json
{
    "Logging": {
        "Console": {
            "FormatterName": "json"
        }
    }
}
```

### Log Shipping
Ship logs to centralized logging system:

```bash
# Install filebeat for log shipping
curl -L -O https://artifacts.elastic.co/downloads/beats/filebeat/filebeat-8.0.0-linux-arm64.tar.gz
tar xzvf filebeat-8.0.0-linux-arm64.tar.gz

# Configure filebeat
sudo nano filebeat.yml

# Add ProdControlAV logs
filebeat.inputs:
- type: journald
  id: prodcontrolav-agent
  units:
    - prodcontrolav-agent.service
```

## Alerting and Notifications

### Email Alerts
Configure email notifications for critical issues:

```bash
# Install mail utils
sudo apt install mailutils

# Configure postfix for email sending
sudo dpkg-reconfigure postfix

# Test email functionality
echo "Test message" | mail -s "Test Subject" admin@yourcompany.com
```

### Webhook Notifications
Send alerts to external systems:

```bash
# Create webhook alert script
sudo tee /usr/local/bin/webhook-alert.sh << 'EOF'
#!/bin/bash
WEBHOOK_URL="https://hooks.slack.com/services/YOUR/SLACK/WEBHOOK"
MESSAGE="$1"
SEVERITY="$2"

curl -X POST -H 'Content-type: application/json' \
    --data "{\"text\":\"Agent Alert: $MESSAGE\", \"color\":\"$SEVERITY\"}" \
    "$WEBHOOK_URL"
EOF

sudo chmod +x /usr/local/bin/webhook-alert.sh
```

### Integration with Monitoring Systems

#### Prometheus Metrics
Expose metrics for Prometheus monitoring:

```json
{
    "Monitoring": {
        "EnablePrometheusMetrics": true,
        "MetricsPort": 9090,
        "MetricsPath": "/metrics"
    }
}
```

#### Custom Metrics
Track custom application metrics:

- Device response times
- API call success rates
- Memory and CPU usage
- Network error rates
- Command execution times

This comprehensive monitoring and troubleshooting guide should help maintain healthy ProdControlAV.Agent deployments and quickly resolve issues when they occur.