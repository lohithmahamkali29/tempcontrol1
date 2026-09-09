using TempControl.Models;

namespace TempControl.Services;

public sealed class ManualControlService
{
    private readonly PlcDataStore _dataStore;
    private readonly ModbusPollingService _pollingService;

    public ManualControlService(PlcDataStore dataStore, ModbusPollingService pollingService)
    {
        _dataStore = dataStore;
        _pollingService = pollingService;
    }

    public async Task<bool> WriteRegisterAsync(int address, double value)
    {
        var plcConfig = GetPlcConfig();
        if (plcConfig is null)
        {
            DiagnosticLogger.Instance.Log("MANUAL", $"Skipped register write D{address} — no PLC config");
            return false;
        }

        var rawValue = (ushort)Math.Clamp((int)Math.Round(value), 0, ushort.MaxValue);
        var success = await _pollingService.WriteRegisterAsync(plcConfig.SlaveId, address, rawValue);
        if (success)
            _dataStore.UpdateRegisterValue(plcConfig.SlaveId, address, rawValue);
        return success;
    }

    public async Task<bool> WriteCoilDirectAsync(int address, bool value)
    {
        var plcConfig = GetPlcConfig();
        if (plcConfig is null)
        {
            DiagnosticLogger.Instance.Log("MANUAL", $"Skipped coil write {address} — no PLC config");
            return false;
        }

        var success = await _pollingService.WriteCoilAsync(plcConfig.SlaveId, address, value);
        if (success)
            _dataStore.UpdateCoilValue(plcConfig.SlaveId, address, value);
        return success;
    }

    public async Task<bool> SetOutputStateAsync(int onAddress, int offAddress, bool turnOn)
    {
        var plcConfig = GetPlcConfig();
        if (plcConfig is null)
        {
            DiagnosticLogger.Instance.Log("MANUAL", $"Skipped coil write {onAddress}/{offAddress} — no PLC config");
            return false;
        }

        var onOk  = await _pollingService.WriteCoilAsync(plcConfig.SlaveId, onAddress, turnOn);
        var offOk = await _pollingService.WriteCoilAsync(plcConfig.SlaveId, offAddress, !turnOn);

        if (onOk)  _dataStore.UpdateCoilValue(plcConfig.SlaveId, onAddress, turnOn);
        if (offOk) _dataStore.UpdateCoilValue(plcConfig.SlaveId, offAddress, !turnOn);

        return onOk && offOk;
    }

    private SlaveDeviceConfig? GetPlcConfig()
        => _dataStore.SlaveConfigs.FirstOrDefault(s => s.SlaveId == 4);
}

