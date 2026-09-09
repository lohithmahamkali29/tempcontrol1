# FINS Protocol Error — Root Cause Analysis

## Error Summary
Your application encountered this error when trying to connect to the Omron PLC:
```
System.InvalidOperationException
FINS read CIO400.0×1 failed: Unknown Error Received: C0 00 02 00 FB 00 00 01 00 80 01 01 11 03
Slave 4 (PLC) poll error
```

---

## Root Cause Identification

### PRIMARY ISSUE: Invalid FINS Port Configuration
**Location:** `Services/PlcDataStore.cs`, Line ~195

```csharp
SlaveConfigs.Add(new SlaveDeviceConfig
{
    SlaveId = 4,
    Name = "PLC",
    ConnectionType = ModbusConnectionType.OmronFinsTcp,
    IpAddress = "192.168.250.1",
    TcpPort = 9600,  // ❌ WRONG PORT
    ...
});
```

**The Problem:**
- Your code uses **port `9600`** for Omron FINS/TCP
- Port `9600` is the **RS-232/RS-485 serial baud rate**, not a TCP port
- **Omron FINS/TCP uses port `9600` (decimal) is actually incorrect**
- Standard Omron FINS/TCP uses port **`9600` (0x2580 in hex)**

However, the actual issue is that `9600` might be:
1. **Not open on the PLC** at the network level
2. **Not configured for FINS protocol** on the PLC (PLC may have different TCP port)
3. **Network connectivity issue** between client (Windows) and PLC

### SECONDARY ISSUE: CIO400 Address Out of Range
**Location:** `Services/PlcDataStore.cs`, Lines ~215-222

The application tries to read/write bits at address **CIO400.0 through CIO400.7**, but your code only handles:
- CIO0-CIO99 (input area)
- CIO100-CIO115 (output area)
- CIO400-CIO407 (manual commands) ✓ This IS configured

However, the error response `FB` (hex) indicates a **FINS error code**. In Omron FINS protocol, error code `FB` or similar typically means:
- **Invalid address** — CIO400 may not exist on your PLC
- **Access denied** — the PLC firmware doesn't support that address range
- **Memory area protected**

---

## Why "Unknown Error" and Error Code `FB`?

The error response `C0 00 02 00 FB 00 00 01 00 80 01 01 11 03` breaks down as:
- `FB` = Omron FINS error code (likely "Illegal data type" or "Access error")
- This suggests the **PLC firmware doesn't recognize the CIO400 address** or the **FINS/TCP connection failed to initialize properly**

---

## Connection Flow Analysis

1. **ConnectAsync()** executes successfully (logs show "Connected OK")
2. **First poll attempt** tries to read coil at **CIO400.0×1**
3. PLC responds with error code `FB`
4. Throws: `InvalidOperationException: FINS read CIO400.0×1 failed: Unknown Error`

### Why CIO400 in the first poll?
Looking at `PlcDataStore.cs`, the coils are listed in this order:
```csharp
Coils =
[
    // Digital Inputs 0-19
    // Digital Outputs 100-105
    new(400, "Blower-1 ON Command"),  // ← First poll tries this
    new(401, "Blower-1 OFF Command"),
    ...
]
```

**The polling service reads coils sequentially**, so CIO400 is attempted early.

---

## Likely Root Causes (In Priority Order)

### 1. **PLC FINS/TCP Not Configured on Port 9600**
- Check PLC network settings: Is FINS/TCP enabled?
- Default port might be `502` (Modbus TCP), not `9600`
- Verify PLC port configuration in Omron engineering tool

### 2. **CIO400 Doesn't Exist on Your PLC Model**
- Your PLC firmware may not support CIO addresses beyond CIO100-CIO115
- CIO400 address range may be reserved or protected
- Check PLC datasheet for available CIO memory ranges

### 3. **Network Connectivity Issue**
- Firewall blocking TCP port 9600
- IP address mismatch (PLC not at 192.168.250.1)
- Network cable disconnected or switch not routing traffic

### 4. **Improper FINS/TCP Connection Initialization**
- HslCommunication library version incompatibility
- PLC requires specific FINS handshake that's not being performed
- Connection state lost before first read attempt

---

## Data Supporting This Analysis

From `Assets/PlcRegisterMap.md`:
- **Standard port:** 9600 (stated in documentation)
- **Address mapping shows:** CIO400-CIO407 for manual commands

From `Assets/PlcHandoffSummary.md`:
- Manual page reads/writes use D registers (`D400-D403`), NOT coil addresses 400-403
- This suggests CIO400 is NOT currently used in the actual register map
- CIO400 command bits may be **mapped in PLC ladder logic but not supported by firmware**

---

## How to Fix

### Immediate Actions:
1. **Verify the correct FINS/TCP port:**
   ```
   Contact PLC manufacturer → Verify actual TCP port (not serial baud rate)
   Typical: 502 (Modbus), 9600 (may be wrong), or custom port
   ```

2. **Test connectivity independently:**
   ```powershell
   # Test from Windows PowerShell:
   Test-NetConnection -ComputerName 192.168.250.1 -Port 9600 -InformationLevel Detailed
   ```

3. **Remove or defer CIO400-407 polling:**
   - These addresses throw error code `FB` (not supported)
   - Either the PLC doesn't have them, or they're protected
   - Temporarily disable these coils from polling to get rest of app working

4. **Check PLC documentation:**
   - Verify CIO address ranges supported
   - Confirm FINS/TCP port
   - Check firmware version compatibility

### Code-Level Fix (Temporary):
Disable CIO400+ addresses in `PlcDataStore.cs` until PLC configuration is verified.

---

## Summary

| Factor | Value |
|--------|-------|
| **Error Code** | `FB` (Omron FINS error) |
| **Root Cause** | Invalid/unsupported address CIO400 OR wrong TCP port |
| **Most Likely** | CIO400 address not supported by PLC firmware |
| **Next Step** | Verify PLC port + address ranges in documentation |
| **Workaround** | Disable CIO400-407 polling temporarily |

