# Movex REST API - Client Onboarding Guide

**Welcome!** This guide will help you get started with the Movex REST API for M3 integration.

---

## Table of Contents
1. [What is the Movex REST API?](#what-is-the-movex-rest-api)
2. [Prerequisites](#prerequisites)
3. [Request Access](#request-access)
4. [Authentication](#authentication)
5. [Quick Start](#quick-start)
6. [Available Endpoints](#available-endpoints)
7. [Testing Your Integration](#testing-your-integration)
8. [CORS Configuration (Web Apps Only)](#cors-configuration-web-apps-only)
9. [Best Practices](#best-practices)
10. [Troubleshooting](#troubleshooting)
11. [Support](#support)

---

## What is the Movex REST API?

The Movex REST API is an internal service that provides a modern HTTP/JSON interface to our M3 ERP system. It allows you to:

- 📦 **Manage Inventory** - Check stock levels, update quantities, transfer items
- 📋 **Query Item Data** - Get item details, pricing, warehouse information
- 🔄 **Execute M3 Transactions** - Run any configured M3 MI program transaction
- ⚡ **Automate Workflows** - Integrate M3 operations into your applications

**Server:** `http://srxwebapp1.srxglobal.com:5000` (or `http://srxwebapp1:5000` internally)

---

## Prerequisites

Before you can use the API, you'll need:

- [ ] **Network Access** - Your application must be able to reach SRXWEBAPP1 on port 5000
- [ ] **API Key** - A unique key assigned to your application (see [Request Access](#request-access))
- [ ] **M3 Knowledge** (Optional) - Basic understanding of M3 concepts (items, warehouses, etc.)
- [ ] **HTTP Client** - Your programming language's HTTP library (curl, fetch, axios, HttpClient, etc.)

---

## Request Access

To get started, you'll need an API key assigned to your application.

### **Step 1: Submit Access Request**

Email the API team with the following information:

**To:** `api-team@srxglobal.com` *(or your internal contact)*  
**Subject:** `Movex API Access Request - [Your Application Name]`

**Include:**
```
Application Name: [e.g., "Warehouse Dashboard", "Inventory Sync Service"]
Purpose: [Brief description of what you'll use the API for]
Environment: [Development, QA, Production]
Contact Person: [Your name and email]
Expected Usage: [Approximate requests per day/hour]

For Web Applications Only:
- Application URL: [e.g., https://warehouse-app.srxglobal.com]
  (This will be added to CORS whitelist)
```

### **Step 2: Receive Your API Key**

You'll receive:
- ✅ **API Key** - A secure random string (e.g., `Ab3xK9mP2qR5sT8vW1yZ4cD7fG0hJ3kL...`)
- ✅ **Key Type** - Either `Primary` (standard access) or `Admin` (elevated privileges)
- ✅ **Documentation Links** - This guide and API reference

### **Step 3: Secure Your API Key**

⚠️ **Important Security Guidelines:**

- **Never commit API keys to Git** - Use environment variables or secrets management
- **Never expose in client-side code** - API keys should only be in backend services
- **Rotate keys periodically** - Request new keys every 6-12 months
- **Don't share keys between environments** - Use separate keys for dev/test/prod
- **Report compromised keys immediately** - Contact the API team to revoke and reissue

**Recommended Storage:**

```bash
# Environment variable (Linux/Mac)
export MOVEX_API_KEY="your-api-key-here"

# Environment variable (Windows)
set MOVEX_API_KEY=your-api-key-here

# .env file (for Node.js, Python, etc.)
MOVEX_API_KEY=your-api-key-here

# Configuration file (gitignored)
config/secrets.json
```

---

## Authentication

All API requests (except `/health`) require authentication using the `X-API-Key` header.

### **Request Format**

```http
GET /api/programs HTTP/1.1
Host: srxwebapp1:5000
X-API-Key: YOUR_API_KEY_HERE
Content-Type: application/json
```

### **Examples by Language**

**PowerShell:**
```powershell
$headers = @{
    "X-API-Key" = $env:MOVEX_API_KEY
}
$response = Invoke-RestMethod -Uri "http://srxwebapp1:5000/api/programs" -Headers $headers
```

**Python:**
```python
import os
import requests

headers = {
    "X-API-Key": os.environ["MOVEX_API_KEY"]
}
response = requests.get("http://srxwebapp1:5000/api/programs", headers=headers)
print(response.json())
```

**JavaScript (Node.js):**
```javascript
const fetch = require('node-fetch');

const headers = {
    'X-API-Key': process.env.MOVEX_API_KEY
};

fetch('http://srxwebapp1:5000/api/programs', { headers })
    .then(res => res.json())
    .then(data => console.log(data));
```

**C# (.NET):**
```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-API-Key", Environment.GetEnvironmentVariable("MOVEX_API_KEY"));

var response = await client.GetAsync("http://srxwebapp1:5000/api/programs");
var data = await response.Content.ReadAsStringAsync();
```

**cURL:**
```bash
curl -H "X-API-Key: $MOVEX_API_KEY" \
  http://srxwebapp1:5000/api/programs
```

---

## Quick Start

### **1. Test Connectivity**

First, verify you can reach the API:

```bash
# Health check (no authentication required)
curl http://srxwebapp1:5000/health
```

**Expected Response:**
```json
{
  "status": "ok",
  "timestamp": "2026-01-14T10:30:00Z",
  "m3Connection": {
    "host": "150.3.2.100",
    "port": 6300,
    "status": "connected"
  }
}
```

### **2. List Available Programs**

See which M3 programs are configured:

```bash
curl -H "X-API-Key: YOUR_KEY" \
  http://srxwebapp1:5000/api/programs
```

**Response:**
```json
[
  "MMS200MI",
  "MMS310MI",
  "MMS003"
]
```

### **3. List Transactions for a Program**

See available transactions for a specific program:

```bash
curl -H "X-API-Key: YOUR_KEY" \
  http://srxwebapp1:5000/api/programs/MMS200MI/transactions
```

**Response:**
```json
[
  "GetItmBasic",
  "GetItmWhsBal"
]
```

### **4. Execute Your First Transaction**

Get basic item information:

```bash
curl -X POST -H "X-API-Key: YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"ITNO": "DC210055"}' \
  http://srxwebapp1:5000/api/transactions/MMS200MI/GetItmBasic
```

**Response:**
```json
{
  "success": true,
  "data": {
    "STAT": 20,
    "ITNO": "DC210055",
    "ITDS": "CAP CER 0.1UF 50V X7R 0402",
    "ITTY": "030",
    "UNMS": "EA",
    "PRGP": "ORIKAN GROUP",
    "ITCL": "CAPS CAPACITORS"
  }
}
```

---

## Available Endpoints

### **System Endpoints**

| Method | Endpoint | Description | Auth Required |
|--------|----------|-------------|---------------|
| `GET` | `/health` | Health check | ❌ No |
| `GET` | `/api/programs` | List configured M3 programs | ✅ Yes |
| `GET` | `/api/programs/{program}/transactions` | List transactions for a program | ✅ Yes |
| `GET` | `/api/transactions/pool-stats` | Connection pool statistics | ✅ Yes |

### **Transaction Endpoints**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/transactions/{program}/{transaction}` | Execute M3 transaction |

**Example:** `/api/transactions/MMS200MI/GetItmBasic`

### **Workflow Endpoints**

Pre-built workflows combining multiple transactions:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/workflows/StockManagement` | Update stock with validation |

---

## Testing Your Integration

### **Step 1: Verify Authentication**

```bash
# Should return 401 Unauthorized
curl http://srxwebapp1:5000/api/programs

# Should return 200 OK with program list
curl -H "X-API-Key: YOUR_KEY" \
  http://srxwebapp1:5000/api/programs
```

### **Step 2: Test Read Operations**

Get item details for a known item:

```bash
curl -X POST -H "X-API-Key: YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"ITNO": "DC210055"}' \
  http://srxwebapp1:5000/api/transactions/MMS200MI/GetItmBasic
```

### **Step 3: Test Write Operations (Carefully!)**

⚠️ **Important:** Test write operations in a **test environment** or with **test data** first!

Example - Update item memo:

```bash
curl -X POST -H "X-API-Key: YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "ITNO": "TEST-ITEM-001",
    "BREM": "Updated via API - Testing"
  }' \
  http://srxwebapp1:5000/api/transactions/MMS310MI/Update
```

### **Step 4: Monitor Connection Pool**

Check API health and connection pool status:

```bash
curl -H "X-API-Key: YOUR_KEY" \
  http://srxwebapp1:5000/api/transactions/pool-stats
```

**Response:**
```json
{
  "totalConnections": 10,
  "availableConnections": 8,
  "activeConnections": 2,
  "poolUtilization": "20%"
}
```

---

## CORS Configuration (Web Apps Only)

**⚠️ This section only applies if you're calling the API from JavaScript in a web browser.**

### **What is CORS?**

CORS (Cross-Origin Resource Sharing) is a browser security feature. If your **web application** runs on a different domain than the API (e.g., your app is on `https://dashboard.srxglobal.com` but the API is on `http://srxwebapp1:5000`), browsers will block the requests unless the API explicitly allows your domain.

### **When Do You Need CORS Configuration?**

✅ **You NEED CORS if:**
- You're calling the API from JavaScript in a web browser
- Your frontend app is on a different URL than the API

❌ **You DON'T NEED CORS if:**
- You're calling from a backend service (Node.js server, Python script, C# service)
- You're using Postman, curl, or other non-browser tools
- You're building a mobile app (iOS/Android)

### **How to Request CORS Access**

Include your application URL in your access request email:

```
Application URL: https://warehouse-app.srxglobal.com

Note: This is a web application that will call the API from users' browsers.
Please add this URL to the AllowedOrigins configuration.
```

The API team will add your URL to the server configuration and restart the service.

### **Testing CORS**

After your URL is added, test from your browser console:

```javascript
fetch('http://srxwebapp1:5000/api/programs', {
  headers: {
    'X-API-Key': 'YOUR_KEY'
  }
})
.then(res => res.json())
.then(data => console.log(data))
.catch(err => console.error('CORS Error:', err));
```

If you see a CORS error like `Access-Control-Allow-Origin`, contact the API team to verify your URL was added correctly.

---

## Best Practices

### **1. Use Environment-Specific Keys**

```javascript
// Good - Different keys per environment
const API_KEY = process.env.NODE_ENV === 'production' 
  ? process.env.PROD_MOVEX_API_KEY 
  : process.env.DEV_MOVEX_API_KEY;

// Bad - Same key everywhere
const API_KEY = "hardcoded-key-123";
```

### **2. Implement Retry Logic**

```python
import time
import requests

def call_api_with_retry(url, headers, max_retries=3):
    for attempt in range(max_retries):
        try:
            response = requests.post(url, headers=headers, timeout=10)
            response.raise_for_status()
            return response.json()
        except requests.exceptions.RequestException as e:
            if attempt < max_retries - 1:
                time.sleep(2 ** attempt)  # Exponential backoff
                continue
            raise
```

### **3. Handle Errors Gracefully**

```javascript
async function getItemDetails(itemNo) {
  try {
    const response = await fetch(`http://srxwebapp1:5000/api/transactions/MMS200MI/GetItmBasic`, {
      method: 'POST',
      headers: {
        'X-API-Key': process.env.MOVEX_API_KEY,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ ITNO: itemNo })
    });
    
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'API request failed');
    }
    
    return await response.json();
  } catch (error) {
    console.error('Failed to get item details:', error);
    // Handle error appropriately (show message, log, retry, etc.)
    throw error;
  }
}
```

### **4. Cache Responses When Appropriate**

```csharp
// Cache item master data (changes infrequently)
private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

public async Task<ItemDetails> GetItemDetails(string itemNo)
{
    var cacheKey = $"item_{itemNo}";
    
    if (_cache.TryGetValue(cacheKey, out ItemDetails cached))
        return cached;
    
    var item = await CallApiAsync(itemNo);
    
    _cache.Set(cacheKey, item, TimeSpan.FromHours(1));
    return item;
}
```

### **5. Log API Interactions**

```python
import logging

logger = logging.getLogger(__name__)

def update_stock(item_no, quantity):
    logger.info(f"Updating stock for {item_no} to {quantity}")
    
    try:
        response = call_movex_api(...)
        logger.info(f"Stock updated successfully for {item_no}")
        return response
    except Exception as e:
        logger.error(f"Failed to update stock for {item_no}: {e}")
        raise
```

### **6. Validate Input Before Sending**

```javascript
function updateStock(itemNo, warehouse, location, quantity) {
  // Validate before calling API
  if (!itemNo || itemNo.length > 15) {
    throw new Error('Invalid item number');
  }
  
  if (!warehouse || warehouse.length !== 3) {
    throw new Error('Warehouse must be 3 characters');
  }
  
  if (quantity < 0) {
    throw new Error('Quantity cannot be negative');
  }
  
  // Now call the API
  return callApi({ ITNO: itemNo, WHLO: warehouse, WHSL: location, STQI: quantity });
}
```

### **7. Monitor Your Usage**

Periodically check connection pool stats to ensure you're not overwhelming the API:

```bash
# Set up monitoring alert if pool utilization > 80%
curl -H "X-API-Key: $API_KEY" \
  http://srxwebapp1:5000/api/transactions/pool-stats
```

---

## Troubleshooting

### **Problem: 401 Unauthorized**

**Cause:** Missing or invalid API key

**Solution:**
1. Verify you're sending the `X-API-Key` header
2. Check that the key value is correct (no extra spaces, line breaks)
3. Confirm your key hasn't been revoked or expired
4. Test with a simple curl command first

```bash
# Test your key
curl -v -H "X-API-Key: YOUR_KEY" http://srxwebapp1:5000/api/programs
```

### **Problem: CORS Error in Browser**

**Cause:** Your application URL isn't in the AllowedOrigins list

**Solution:**
1. Verify you're calling from a browser (not Postman/backend)
2. Check the exact URL (including protocol: `http://` vs `https://`)
3. Contact API team to add your URL
4. Note: `localhost` URLs need to be added for local development

### **Problem: Connection Timeout**

**Cause:** Network issue, server overload, or M3 is slow

**Solution:**
1. Check network connectivity: `Test-NetConnection srxwebapp1 -Port 5000`
2. Verify API is running: `curl http://srxwebapp1:5000/health`
3. Check pool stats for high utilization
4. Implement retry logic with exponential backoff
5. Contact API team if persistent

### **Problem: Transaction Returns Error**

**Cause:** Invalid input parameters or M3 business rule violation

**Solution:**
1. Check the error message in the response
2. Verify all required fields are provided
3. Validate field lengths and formats (M3 is strict!)
4. Test the same transaction in M3 directly
5. Review transaction configuration in `/transactions/{program}.json`

**Common M3 Errors:**

| Error | Meaning | Solution |
|-------|---------|----------|
| `Item does not exist` | ITNO not found | Verify item number in M3 |
| `Warehouse not valid` | Invalid WHLO | Check warehouse code (3 chars) |
| `Negative stock not allowed` | Insufficient quantity | Check current balance first |
| `Field too long` | Input exceeds field length | Trim input to max length |

### **Problem: Slow Response Times**

**Cause:** M3 performance, network latency, or connection pool exhaustion

**Solution:**
1. Check pool stats: `curl http://srxwebapp1:5000/api/transactions/pool-stats`
2. Batch requests instead of individual calls
3. Implement caching for frequently accessed data
4. Consider running during off-peak hours for bulk operations
5. Contact API team if consistently slow

---

## Support

### **Getting Help**

For issues or questions:

1. **Check the logs** - Review error messages in API responses
2. **Review this guide** - Check troubleshooting section
3. **Test with curl** - Isolate if it's your code or the API
4. **Contact the API team**

### **API Team Contact**

**Email:** `api-team@srxglobal.com`  
**Teams/Slack:** `#movex-api-support`  
**Response Time:** Within 1 business day

### **Provide This Information When Requesting Support**

- Your application name and API key (first 8 characters only)
- Timestamp of the issue (with timezone)
- Endpoint and HTTP method you're calling
- Request payload (sanitize any sensitive data)
- Error message or response code
- Any relevant logs from your application

### **Example Support Request**

```
Subject: API Error - 500 Internal Server Error on Stock Update

Application: Warehouse Dashboard
API Key: Ab3xK9mP... (Primary)
Timestamp: 2026-01-14 14:35:22 EST

Endpoint: POST /api/transactions/MMS310MI/Update
Request Payload:
{
  "ITNO": "DC210055",
  "BREM": "Test update"
}

Error Response:
{
  "error": "Internal server error",
  "message": "Connection pool exhausted"
}

Additional Info:
- This happens intermittently during peak hours (2-4 PM)
- Works fine in the morning
- Pool stats show 100% utilization

Can you help investigate?
```

---

## Additional Resources

- **API Documentation:** `/docs/API_REFERENCE.md` (in repository)
- **Deployment Guide:** `/docs/IIS_DEPLOYMENT_SRXWEBAPP1.md`
- **Transaction Configs:** `/transactions/` directory
- **M3 Documentation:** Internal M3 wiki or Infor support

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-05-25 | 1.1 | `GetItmBasic` response now includes `PRGP` (Procurement Group) and `ITCL` (Product Group) |
| 2026-01-14 | 1.0 | Initial release |

---

**Happy Integrating! 🚀**

If you have suggestions to improve this guide, please contact the API team or submit a pull request.
