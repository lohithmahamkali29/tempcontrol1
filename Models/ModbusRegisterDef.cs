namespace TempControl.Models;

/// <summary>
/// Defines a single Modbus register or coil address to poll from a slave device.
/// Each address is explicitly listed — no assumption of sequential layout.
/// </summary>
public record ModbusRegisterDef(int Address, string Name);
