using TempControl.Models;

namespace TempControl.Services;

/// <summary>
/// A simulated IModbusTransport used when no physical PLC is available.
/// ConnectAsync succeeds instantly; reads return realistic oscillating values
/// for all known PLC register addresses; writes are silently ignored.
/// Switch the app to offline/simulate mode by setting MainViewModel.OfflineMode = true.
/// </summary>
internal sealed class NullModbusTransport : IModbusTransport
{
    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        DiagnosticLogger.Instance.Log("NULL", "Simulate mode — connect skipped");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<ushort[]> ReadHoldingRegistersAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = SimulateRegister(slaveId, startAddress + i);
        return Task.FromResult(result);
    }

    public Task<bool[]> ReadCoilsAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var result = new bool[count];
        for (int i = 0; i < count; i++)
            result[i] = SimulateCoil(slaveId, startAddress + i);
        return Task.FromResult(result);
    }

    public Task WriteSingleRegisterAsync(int slaveId, int address, ushort value, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task WriteSingleCoilAsync(int slaveId, int address, bool value, CancellationToken ct = default)
        => Task.CompletedTask;

    public void Dispose() { }

    // ── Simulation helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Slow sine wave (0–1) driven by real wall-clock time, so values drift
    /// smoothly every few seconds without any state.
    /// </summary>
    private static double Wave(double periodSeconds = 10.0, double phase = 0.0)
    {
        double t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        return (Math.Sin(2 * Math.PI * t / periodSeconds + phase) + 1.0) / 2.0; // 0..1
    }

    /// <summary>
    /// Returns a simulated ushort for the given slave/address.
    /// Energy values are exposed as float32 with little-endian word order and byte swap.
    /// Even address = LSW register, odd address = MSW register.
    /// </summary>
    private static ushort SimulateRegister(int slaveId, int address)
    {
        // Slave 2 = Zone 1 temperature controller (RS485)
        // Slave 3 = Zone 2 temperature controller (RS485)
        if (slaveId == 2)
        {
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return address switch
            {
                0x1000 => (ushort)(30  + (int)(ramp * 220 + Wave(8.0, 0.0) * 6)),  // Zone1 PV:  30→250 °C
                0x1001 => (ushort)(250 + (int)(Wave(60.0, 0.0) * 50)),              // Zone1 SV:  250–300 °C
                0x1002 => (ushort)(15  + (int)(Wave(20.0, 0.0) * 70)),              // Zone1 Output: 15–85 %
                _ => 0,
            };
        }

        if (slaveId == 3)
        {
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return address switch
            {
                0x1000 => (ushort)(25  + (int)(ramp * 215 + Wave(9.0, 1.0) * 6)),  // Zone2 PV:  25→240 °C (offset from Zone1)
                0x1001 => (ushort)(240 + (int)(Wave(60.0, 1.5) * 50)),              // Zone2 SV:  240–290 °C (offset from Zone1)
                0x1002 => (ushort)(10  + (int)(Wave(25.0, 1.5) * 65)),              // Zone2 Output: 10–75 % (offset from Zone1)
                _ => 0,
            };
        }

        // All MFM / process data is on slave 4
        if (slaveId != 4) return 0;

        double w = Wave();   // 0..1, slow drift

        // ── Zone temperatures (D100–D101) — PV oscillates like a real oven ──
        // Setpoint = 250 °C; PV ramps up then holds with slight ripple
        if (address == 120)
        {
            // D120 is the PLC's Zone 1 PV register used by PlcDataStore.
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return (ushort)(30 + (int)(ramp * 220 + Wave(8.0) * 6));
        }
        if (address == 121)
        {
            // D121 is the PLC's Zone 2 PV register used by PlcDataStore.
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return (ushort)(30 + (int)(ramp * 218 + Wave(9.0, 1.0) * 6));
        }

        if (address == 100)
        {
            // Ramp from 30 °C to 250 °C over ~5 min, then hold with ±3 °C ripple
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return (ushort)(30 + (int)(ramp * 220 + Wave(8.0) * 6));
        }
        if (address == 101)
        {
            double ramp = Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0);
            return (ushort)(30 + (int)(ramp * 218 + Wave(9.0, 1.0) * 6));
        }

        // ── MFM Total Power voltages (D126–D131) — float32 ───────────────
        // R-Phase Voltage ~413 V ± 5 V
        if (address == 126) return FloatLswOf(413.0f + (float)(w * 5.0));
        if (address == 127) return FloatMswOf(413.0f + (float)(w * 5.0));
        // Y-Phase Voltage
        if (address == 128) return FloatLswOf(412.5f + (float)(w * 4.8));
        if (address == 129) return FloatMswOf(412.5f + (float)(w * 4.8));
        // B-Phase Voltage
        if (address == 130) return FloatLswOf(412.8f + (float)(w * 5.2));
        if (address == 131) return FloatMswOf(412.8f + (float)(w * 5.2));

        // ── MFM Total Power currents (D132–D137) — float32 ───────────────
        // R-Phase Current ~20 A ± 5 A
        if (address == 132) return FloatLswOf(18.0f + (float)(w * 5.0));
        if (address == 133) return FloatMswOf(18.0f + (float)(w * 5.0));
        // Y-Phase Current
        if (address == 134) return FloatLswOf(18.5f + (float)(Wave(12.0, 1.0) * 4.8));
        if (address == 135) return FloatMswOf(18.5f + (float)(Wave(12.0, 1.0) * 4.8));
        // B-Phase Current
        if (address == 136) return FloatLswOf(18.2f + (float)(Wave(14.0, 2.0) * 4.6));
        if (address == 137) return FloatMswOf(18.2f + (float)(Wave(14.0, 2.0) * 4.6));

        // ── Total KW (D138–D139) — float32 ───────────────────────────────
        if (address == 138) return FloatLswOf(45.0f + (float)(w * 10.0));
        if (address == 139) return FloatMswOf(45.0f + (float)(w * 10.0));

        // ── Zone-1 Phase Currents (D140–D145) — float32 ──────────────────
        if (address == 140) return FloatLswOf(9.0f + (float)(w * 2.0));
        if (address == 141) return FloatMswOf(9.0f + (float)(w * 2.0));
        if (address == 142) return FloatLswOf(8.8f + (float)(Wave(11.0) * 1.8));
        if (address == 143) return FloatMswOf(8.8f + (float)(Wave(11.0) * 1.8));
        if (address == 144) return FloatLswOf(9.1f + (float)(Wave(13.0) * 1.9));
        if (address == 145) return FloatMswOf(9.1f + (float)(Wave(13.0) * 1.9));

        // ── Zone-2 Phase Currents (D160–D165) — float32 ──────────────────
        if (address == 160) return FloatLswOf(8.7f + (float)(Wave(9.0, 0.5) * 2.1));
        if (address == 161) return FloatMswOf(8.7f + (float)(Wave(9.0, 0.5) * 2.1));
        if (address == 162) return FloatLswOf(8.6f + (float)(Wave(10.0, 1.5) * 2.0));
        if (address == 163) return FloatMswOf(8.6f + (float)(Wave(10.0, 1.5) * 2.0));
        if (address == 164) return FloatLswOf(8.9f + (float)(Wave(8.0, 2.5) * 1.95));
        if (address == 165) return FloatMswOf(8.9f + (float)(Wave(8.0, 2.5) * 1.95));

        // ── Process step parameters (D301–D320) — oscillating to verify Settings page live update ──
        return address switch
        {
            // D123 — Zone1 job thermocouple PV
            123 => (ushort)(20  + (int)(Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0) * 210 + Wave(8.0,  0.5) * 8)),
            // D125 — Zone2 job thermocouple PV
            125 => (ushort)(18  + (int)(Math.Min(1.0, DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600 / 300.0) * 205 + Wave(9.0,  1.2) * 8)),

            // Step 1: Zone temps drift ±10 °C, soak ±5 min, ramp fixed
            301 => (ushort)(245 + (int)(Wave(45.0, 0.0) * 20)), // Zone1 Temp: 245–265
            302 => (ushort)(245 + (int)(Wave(50.0, 0.5) * 20)), // Zone2 Temp: 245–265
            303 => (ushort)(28  + (int)(Wave(40.0, 1.0) * 10)), // Soak Time:   28–38 min
            304 => 5,

            // Step 2
            305 => (ushort)(295 + (int)(Wave(55.0, 1.5) * 20)),
            306 => (ushort)(295 + (int)(Wave(60.0, 2.0) * 20)),
            307 => (ushort)(43  + (int)(Wave(35.0, 0.0) * 10)),
            308 => 5,

            // Step 3
            309 => (ushort)(345 + (int)(Wave(50.0, 2.5) * 20)),
            310 => (ushort)(345 + (int)(Wave(45.0, 0.3) * 20)),
            311 => (ushort)(58  + (int)(Wave(40.0, 1.5) * 10)),
            312 => 5,

            // Step 4
            313 => (ushort)(395 + (int)(Wave(60.0, 0.8) * 20)),
            314 => (ushort)(395 + (int)(Wave(55.0, 1.2) * 20)),
            315 => (ushort)(88  + (int)(Wave(35.0, 2.0) * 10)),
            316 => 5,

            // Step 5
            317 => (ushort)(195 + (int)(Wave(45.0, 1.8) * 20)),
            318 => (ushort)(195 + (int)(Wave(50.0, 0.6) * 20)),
            319 => (ushort)(18  + (int)(Wave(40.0, 2.5) * 10)),
            320 => 5,

            // Global settings — safety and blower drift slightly
            321 => (ushort)(445 + (int)(Wave(120.0, 0.0) * 20)), // Zone1 Safety: 445–465
            322 => (ushort)(445 + (int)(Wave(120.0, 1.0) * 20)), // Zone2 Safety: 445–465
            323 => (ushort)(48  + (int)(Wave(90.0,  0.5) * 10)), // Blower1:       48–58
            324 => (ushort)(48  + (int)(Wave(90.0,  1.5) * 10)), // Blower2:       48–58

            325 => (ushort)(200 + (int)(Wave(60.0, 0.0) * 100)), // Setpoint Z1: 200–300
            326 => (ushort)(200 + (int)(Wave(60.0, 1.0) * 100)), // Setpoint Z2: 200–300
            327 => (ushort)(420 + (int)(Wave(90.0, 0.5) *  60)), // Safety Z1:   420–480
            328 => (ushort)(420 + (int)(Wave(90.0, 1.5) *  60)), // Safety Z2:   420–480

            400 => 1,
            401 => (ushort)(DateTime.UtcNow.Minute % 30),
            402 => 30,
            _ => 0,
        };
    }

    private static bool SimulateCoil(int slaveId, int address)
    {
        if (slaveId != 4) return false;

        // Most digital inputs = true (healthy state)
        if (address == 10000) return true;  // Single Phase Preventer OK
        if (address == 10001) return true;  // Emergency Switch OK (NC = true)
        if (address == 10002) return true;  // Door Limit Switch Close
        if (address == 10003) return true;  // Blower motor-1 feedback
        if (address == 10004) return true;  // Blower motor-2 feedback
        if (address == 10005) return true;  // Exhaust Blower feedback
        if (address == 10017) return true;  // Blower-1 Cont. ON
        if (address == 10018) return true;  // Blower-2 Cont. ON
        if (address == 10024) return true;  // IsManualMode = AUTO (enables RUN button in offline test)
        if (address == 10644) return false; // W40.4 run bit — OFF at startup, writes are no-ops
        if (address == 10019) return Wave() > 0.3;  // Heater-1 cycles
        if (address == 10020) return Wave(12.0) > 0.3;  // Heater-2 cycles
        if (address == 10704) return true;  // Process running

        // Outputs: heaters / blowers ON
        if (address == 1601) return true;   // Heater-1 Cont.
        if (address == 1602) return true;   // Heater-2 Cont.
        if (address == 1603) return true;   // Blower motor-1 Cont.
        if (address == 1604) return true;   // Blower motor-2 Cont.
        if (address == 1607) return true;   // TOWER GREEN

        return false;
    }

    private static ushort FloatLswOf(float value)
    {
        uint bits = (uint)BitConverter.SingleToInt32Bits(value);
        return SwapBytes((ushort)(bits & 0xFFFF));
    }

    private static ushort FloatMswOf(float value)
    {
        uint bits = (uint)BitConverter.SingleToInt32Bits(value);
        return SwapBytes((ushort)(bits >> 16));
    }

    private static ushort SwapBytes(ushort value)
        => (ushort)((value >> 8) | (value << 8));
}

/// <summary>
/// Factory that returns a NullModbusTransport for every slave config.
/// </summary>
public sealed class NullTransportFactory : IModbusTransportFactory
{
    public IModbusTransport CreateTransport(SlaveDeviceConfig config)
        => new NullModbusTransport();
}
