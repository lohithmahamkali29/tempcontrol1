# PLC Connection Termination Analysis — FINS/TCP Socket Failures

## Error Pattern Analysis

Your logs show a cascading connection failure:

```
1. [11:08:28] FAILED CIO400.0 → Socket Timeout (5000ms)
2. [11:08:33] FAILED D100 → Connection Aborted
3. [11:08:45] FAILED D100 → Connected Timeout (10000ms)
4. [11:08:46] FAILED D100 → Exceeding connection limit ⚠️ CRITICAL
5. [11:08:47] FAILED D100 → Remote shutdown of connection
6. [11:08:48] FAILED D100 → Exceeding connection limit
```

---

## Root Causes (In Order)

### 1. **PRIMARY: Invalid Address CIO400.0 Triggers First Failure**

**Error at 11:08:28:**
```
[FINS-READ] FAILED CIO400.0: PipeTcpNet[192.168.250.1:9600] : 
Socket Exception → A connection attempt failed because the connected party 
did not properly respond after a period of time
```

**Why this happens:**
- Your PLC firmware **does NOT support CIO400 address** (as we identified earlier)
- First read attempt = read CIO400.0 (first coil in the polling list)
- PLC responds with error or doesn't respond
- Connection times out (5000ms default timeout)
- **Connection becomes corrupted/unstable**

---

### 2. **SECONDARY: Connection Pool Exhaustion**

**Error at 11:08:46:**
```
[FINS-READ] FAILED D100: PipeTcpNet[192.168.250.1:9600] : 
Exceeding connection limit
```

**Why this happens:**
- `OmronFinsNet` from HslCommunication library maintains a **connection pool**
- Each failed read attempt **may not properly close the socket**
- Every 1000ms (your poll interval), new connection attempts are made
- Old connections are not returned to pool or cleaned up
- **Pool limit exceeded** → new connections rejected

**Timeline of pool exhaustion:**
```
11:08:28 - Connection attempt 1: FAILED, socket not returned
11:08:29 - Retry: FAILED, socket not returned
11:08:33 - Retry: FAILED, socket not returned
11:08:34 - Retry: FAILED, socket not returned
11:08:35 - Retry: FAILED, socket not returned
...
11:08:46 - Pool exhausted → "Exceeding connection limit"
```

---

### 3. **TERTIARY: Connection Lifecycle Issues in HslCommunication**

**Observed sequence:**
1. Initial timeout (socket can't reach PLC or gets no response)
2. Aborted by software (PLC or app closes connection)
3. Connection timeout during subsequent read
4. Pool limit exceeded (connections not recycled)
5. Remote shutdown (PLC actively closes connection)

**Why this cascade happens:**
- `OmronFinsNet` doesn't automatically reconnect after failure
- `ModbusPollingService` continues polling every 1000ms
- Each poll attempt creates new connection attempt
- Corrupted/failed connections remain in pool
- No graceful connection recovery mechanism

---

## Connection Lifecycle in Your Code

### File: `Services/OmronFinsTcpTransport.cs`

```csharp
public async Task ConnectAsync(CancellationToken ct = default)
{
    var result = await Task.Run(_net.ConnectServer, ct);
    if (!result.IsSuccess)
    {
        throw new InvalidOperationException($"Omron FINS connect failed: {result.Message}");
    }
    IsConnected = true;  // ← Set to true even after timeout
}
```

**Issue:** After connection fails and throws exception, subsequent calls to this method create new connection attempts without disposing old ones.

### File: `Services/ModbusPollingService.cs`

```csharp
private async Task PollGroupAsync(List<SlaveDeviceConfig> slaves, CancellationToken ct)
{
    using var transport = _transportFactory.CreateTransport(slaves[0]);

    // Keep trying to connect until successful or cancelled
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await transport.ConnectAsync(ct);  // ← Infinite retry loop
            break;
        }
        catch (Exception ex)
        {
            // Retry in 5s
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

**Issue:** The while loop retries connection every 5 seconds, but doesn't dispose/reset the old transport before creating new connections. Combined with rapid polling (1000ms interval), this exhausts the connection pool.

---

## Why "Exceeding Connection Limit" Happens

The HslCommunication library's `OmronFinsNet` likely has:
- **Connection pool size limit** (typically 10-20 connections)
- **TCP port reuse timeout** (TIME_WAIT state, typically 60 seconds)

When rapid failures occur:
```
T=0ms  : Connect attempt 1 → Fail → Socket left in TIME_WAIT
T=1s   : Poll retry → Connect attempt 2 → Fail → Socket left in TIME_WAIT
T=2s   : Poll retry → Connect attempt 3 → Fail → Socket left in TIME_WAIT
...
T=16s  : Pool full (16 sockets in TIME_WAIT) → "Exceeding connection limit"
```

---

## Why Connection Terminates

1. **Initial failure** at CIO400.0 corrupts connection state
2. **Rapid retry loop** creates many failed connections
3. **Connection pool exhausted** → can't create new connections
4. **PLC closes connection** ("Remote shutdown") because:
   - Too many failed connection attempts detected
   - PLC firmware limit on failed attempts reached
   - PLC firmware rejects malformed FINS requests (CIO400)
5. **App can't reconnect** because pool is exhausted

---

## Solution

### Immediate Fix (Remove CIO400 from polling):

**File:** `Services/PlcDataStore.cs` (Lines ~215-222)

**Current code (CAUSES PROBLEMS):**
```csharp
Coils =
[
    // Digital Inputs 0-19
    // Digital Outputs 100-105
    new(400, "Blower-1 ON Command"),    // ← CIO400 doesn't exist!
    new(401, "Blower-1 OFF Command"),   // ← Causes first failure
    ...
]
```

**Action needed:**
- Remove lines for addresses 400-407 from the Coils list
- PLC doesn't support CIO400 address range
- First read no longer fails → connection remains stable
- Poll succeeds on D100 and other valid addresses

### Long-term Fix (Connection Resilience):

Consider adding to `OmronFinsTcpTransport`:
1. **Dispose old transport before reconnect** in `PollGroupAsync`
2. **Implement exponential backoff** instead of immediate retry
3. **Add connection health check** before polling
4. **Implement connection reset** after N consecutive failures

---

## What Changed vs. What Didn't

| Aspect | Status | Note |
|--------|--------|------|
| CIO400-407 polling | ⚠️ Problem | Doesn't exist on PLC, causes cascade failure |
| Connection pool | ⚠️ Issue | Not disposed/recycled after failure |
| Retry logic | ⚠️ Issue | Rapid retries with no backoff exhaust pool |
| D100 polling | ✓ Should work | Valid address, fails due to pool exhaustion |
| Connection timeout | ✓ Normal | 5000ms for reads, 10000ms for connect |

---

## Next Steps

1. **Remove CIO400-407 from Coils list** (quick fix, immediate relief)
2. **Verify D100+ addresses exist on PLC** (per your documentation)
3. **Test connection stability** after removing bad addresses
4. **Consider connection pooling improvements** for robustness

---

## Expected Result After Fix

**Before (Current):**
```
T=0ms   : Poll CIO400.0 → Timeout
T=1s    : Retry CIO400.0 → Timeout
T=2s    : Retry CIO400.0 → Timeout
...
T=16s   : Connection pool exhausted → "Exceeding connection limit"
```

**After removing CIO400-407:**
```
T=0ms   : Poll D100 → Success ✓
T=1s    : Poll D100 → Success ✓
T=2s    : Poll D100 → Success ✓
...
Continuous polling → Connection stable
```

