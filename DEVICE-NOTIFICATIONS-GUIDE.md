# Device Notifications Feature - Configuration Guide

## Overview

The Device Notifications feature allows Pro plan users to receive SMS notifications when monitored devices go offline. This guide covers setup, configuration, and security considerations.

## Features

- **Subscription Plans**: Base (free) and Pro (with SMS notifications)
- **SMS Notifications**: Real-time text alerts when devices go offline
- **Secure Phone Storage**: Phone numbers are encrypted at rest using ASP.NET Core Data Protection API
- **Rate Limiting**: Prevents SMS spam with 60-minute cooldown between notifications per device
- **User Opt-In**: Users must explicitly enable SMS notifications

## Prerequisites

- Twilio account with active phone number
- .NET 8 SDK
- Azure SQL Database (for user data)
- Azure Table Storage (for device status)

## Configuration

### 1. Twilio Setup

1. Create a Twilio account at https://www.twilio.com/
2. Purchase a phone number with SMS capabilities
3. Note your Account SID, Auth Token, and phone number

### 2. Environment Variables

Add the following to your environment or Azure App Service configuration:

```bash
TWILIO_ACCOUNT_SID=ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
TWILIO_AUTH_TOKEN=your_auth_token_here
TWILIO_FROM_PHONE_NUMBER=+15551234567
```

### 3. appsettings.json Configuration

The application is already configured to read Twilio settings from environment variables:

```json
{
  "Twilio": {
    "AccountSid": "${TWILIO_ACCOUNT_SID}",
    "AuthToken": "${TWILIO_AUTH_TOKEN}",
    "FromPhoneNumber": "${TWILIO_FROM_PHONE_NUMBER}"
  }
}
```

### 4. Database Migration

Run the database migration to add subscription plan fields:

```bash
cd src/ProdControlAV.API
dotnet ef database update
```

This adds:
- `SubscriptionPlan` (enum: Base = 0, Pro = 1)
- `PhoneNumber` (encrypted, max 500 chars)
- `SmsNotificationsEnabled` (boolean)

## Usage

### For Users

1. **Navigate to Account Settings**
   - Log in to ProdControlAV
   - Click "Account" in the navigation menu

2. **Upgrade to Pro Plan**
   - Click "Upgrade to Pro" button
   - Plan upgrade is immediate (no payment integration yet)

3. **Enable SMS Notifications**
   - Enter phone number in E.164 format (e.g., `+15551234567`)
   - Check "Enable SMS notifications"
   - Click "Save Notification Settings"

4. **Receive Alerts**
   - When a device goes offline, you'll receive an SMS:
   ```
   PROD-CONTROL: Alert - Camera-1 is offline! Last seen: 5m ago
   ```

### For Administrators

#### API Endpoints

**Get Current Plan**
```
GET /api/userplan
Response: { "currentPlan": "Base", "canUpgrade": true, "smsNotificationsEnabled": false, "maskedPhoneNumber": null }
```

**Upgrade Plan**
```
POST /api/userplan/upgrade
Body: { "newPlan": "Pro" }
Response: { "currentPlan": "Pro", "canUpgrade": false, ... }
```

**Update SMS Preferences**
```
POST /api/userplan/sms-preferences
Body: { "phoneNumber": "+15551234567", "smsNotificationsEnabled": true }
Response: { "currentPlan": "Pro", "smsNotificationsEnabled": true, "maskedPhoneNumber": "***-***-4567" }
```

## Security Considerations

### Phone Number Encryption

Phone numbers are encrypted using ASP.NET Core Data Protection API:
- Encryption keys are stored securely in the data protection system
- Keys are automatically rotated
- Encrypted values are unique per installation

### Phone Number Validation

- Only E.164 format accepted: `+[country code][number]`
- Regex validation: `^\+[1-9]\d{1,14}$`
- Invalid formats are rejected with clear error messages

### Rate Limiting

- 60-minute cooldown between SMS notifications per device
- Prevents accidental SMS spam
- Configurable in `DeviceOfflineNotificationService.cs`

### Authorization

- SMS preferences endpoints require authentication
- Only Pro plan users can enable SMS notifications
- Users can only modify their own settings

## Architecture

### Components

1. **DeviceOfflineNotificationService** (Background Service)
   - Polls device status every 30 seconds
   - Detects online → offline transitions
   - Sends SMS to Pro users with notifications enabled
   - Rate limits notifications per device

2. **TwilioSmsService** (Infrastructure)
   - Wraps Twilio SDK
   - Handles SMS sending with error handling
   - Logs all SMS operations (with masked phone numbers)

3. **UserPlanController** (API)
   - Manages subscription plans
   - Handles SMS preference updates
   - Encrypts/decrypts phone numbers

4. **AccountSettings.razor** (Blazor UI)
   - User-friendly plan management interface
   - Phone number input with validation
   - Real-time feedback on save

### Data Flow

```
Device goes offline → Table Storage status update
                   ↓
      DeviceOfflineNotificationService (polls every 30s)
                   ↓
      Detects transition (online → offline)
                   ↓
      Query Pro users with SMS enabled for tenant
                   ↓
      Decrypt phone numbers
                   ↓
      TwilioSmsService sends SMS
                   ↓
      Record last notification time (rate limiting)
```

## Monitoring

### Logs

All SMS operations are logged with appropriate levels:

```
[Information] SMS sent successfully. SID: SM..., Status: queued
[Warning] Cannot send SMS: Twilio not configured
[Error] Failed to send SMS. Error code: 21211, Message: Invalid phone number
```

### Application Insights

- SMS send success/failure rates
- Device offline detection timing
- User upgrade events

## Troubleshooting

### SMS Not Sending

1. **Check Twilio Configuration**
   ```bash
   # Verify environment variables are set
   echo $TWILIO_ACCOUNT_SID
   echo $TWILIO_FROM_PHONE_NUMBER
   ```

2. **Check Twilio Logs**
   - Log in to Twilio Console
   - Navigate to Messaging → Logs
   - Check for failed message attempts

3. **Verify Phone Number Format**
   - Must start with `+`
   - Country code required
   - No spaces or special characters except `+`

### Notifications Not Received

1. **Verify Plan Status**
   - User must have Pro plan
   - SMS notifications must be enabled
   - Phone number must be saved

2. **Check Rate Limiting**
   - Only one notification per device per 60 minutes
   - Check logs for "Skipping notification" messages

3. **Verify Device Status**
   - Agent must be running and reporting status
   - Device must actually transition online → offline

## Cost Considerations

### Twilio Pricing

- **SMS (US)**: ~$0.0075 per message
- **SMS (International)**: Varies by country (up to $0.10+)
- **Phone Number**: ~$1.15/month

### Example Monthly Costs

**Small Deployment** (10 devices, 5 Pro users)
- Assume 2 offline events per device per month
- 10 devices × 2 events × 5 users = 100 SMS
- Cost: 100 × $0.0075 = **$0.75/month** + phone number

**Medium Deployment** (50 devices, 20 Pro users)
- 50 × 2 × 20 = 2,000 SMS
- Cost: 2,000 × $0.0075 = **$15/month** + phone number

### Cost Optimization

1. **Rate Limiting**: Already implemented (60 min per device)
2. **Batch Notifications**: Consider daily digest instead of real-time (future)
3. **Email Alternative**: Add email notifications as free alternative (future)

## Future Enhancements

- [ ] Email notification option (free alternative to SMS)
- [ ] Notification preferences per device
- [ ] Daily digest mode (batch notifications)
- [ ] Custom message templates
- [ ] Webhook integration for third-party services
- [ ] Payment integration for Pro plan
- [ ] Notification history/audit log

## Testing

### Unit Tests

```bash
# Run SMS service tests
dotnet test --filter "FullyQualifiedName~TwilioSmsServiceTests"

# Run plan management tests
dotnet test --filter "FullyQualifiedName~UserPlanControllerTests"
```

### Manual Testing

1. **Test SMS Configuration**
   - Set valid Twilio credentials
   - Restart API service
   - Check logs for "Twilio SMS service initialized successfully"

2. **Test Plan Upgrade**
   - Navigate to /account
   - Click "Upgrade to Pro"
   - Verify badge changes to "Pro Plan"

3. **Test SMS Notifications**
   - Enable SMS with valid phone number
   - Stop an agent to simulate device offline
   - Wait ~30 seconds for notification
   - Verify SMS received

## Support

For issues or questions:
- GitHub Issues: https://github.com/APoythress/ProdControlAV/issues
- Documentation: https://github.com/APoythress/ProdControlAV/blob/main/README.md
