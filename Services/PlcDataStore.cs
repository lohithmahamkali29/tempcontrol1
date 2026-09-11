using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TempControl.Models;

namespace TempControl.Services;

/// <summary>
/// Central data store holding all PLC I/O values.
/// Updated by the Modbus polling service and observed by ViewModels.
/// Designed so that multiple RS485 slaves can write to it concurrently.
/// </summary>
public partial class PlcDataStore : ObservableObject
{
    // ?? Zone Temperatures (from RS485 temperature controller slaves) ??
    [ObservableProperty] private double _zone1Temperature;
    [ObservableProperty] private double _zone1Setpoint = 200.0;
    [ObservableProperty] private double _zone1Output;
    [ObservableProperty] private double _zone1SafetyTemperature;
    [ObservableProperty] private double _zone1JobTemperature;  // D123 � Zone1 job thermocouple PV
    [ObservableProperty] private double _zone1SetPointValueManual;       // D124 � Zone1 ramp rate (�C/min)
    [ObservableProperty] private double _zone2Temperature;
    [ObservableProperty] private double _zone2Setpoint = 200.0;
    [ObservableProperty] private double _zone2Output;
    [ObservableProperty] private double _zone2SafetyTemperature;
    [ObservableProperty] private double _zone2JobTemperature;  // D125 � Zone2 job thermocouple PV

    [ObservableProperty] private bool _blower1ManualStatus;
    [ObservableProperty] private bool _blower2ManualStatus;
    [ObservableProperty] private bool _heater1ManualStatus;
    [ObservableProperty] private bool _heater2ManualStatus;

    // ?? MFM Meter readings (from RS485 MFM meter slave) ??
    [ObservableProperty] private double _voltageR;
    [ObservableProperty] private double _voltageY;
    [ObservableProperty] private double _voltageB;
    [ObservableProperty] private double _currentR;
    [ObservableProperty] private double _currentY;
    [ObservableProperty] private double _currentB;
    [ObservableProperty] private double _totalPowerKw;

    // ?? Zone-1 Power (individual phase currents) ??
    [ObservableProperty] private double _zone1CurrentR;
    [ObservableProperty] private double _zone1CurrentY;
    [ObservableProperty] private double _zone1CurrentB;

    // ?? Zone-2 Power (individual phase currents) ??
    [ObservableProperty] private double _zone2CurrentR;
    [ObservableProperty] private double _zone2CurrentY;
    [ObservableProperty] private double _zone2CurrentB;

    // ?? Process State ??
    [ObservableProperty] private string _chamberStatus = "IDLE";
    [ObservableProperty] private double _elapsedTime;
    [ObservableProperty] private TimeSpan _remainingTime;
    [ObservableProperty] private double _soakTime;
    // ?? Per-Step Process Parameters (5 steps � 4 values) ??
    [ObservableProperty] private double _step1Zone1Temp;
    [ObservableProperty] private double _step1Zone2Temp;
    [ObservableProperty] private double _step1SoakTime;
    [ObservableProperty] private double _step1RampRate;

    [ObservableProperty] private double _step2Zone1Temp;
    [ObservableProperty] private double _step2Zone2Temp;
    [ObservableProperty] private double _step2SoakTime;
    [ObservableProperty] private double _step2RampRate;

    [ObservableProperty] private double _step3Zone1Temp;
    [ObservableProperty] private double _step3Zone2Temp;
    [ObservableProperty] private double _step3SoakTime;
    [ObservableProperty] private double _step3RampRate;

    [ObservableProperty] private double _step4Zone1Temp;
    [ObservableProperty] private double _step4Zone2Temp;
    [ObservableProperty] private double _step4SoakTime;
    [ObservableProperty] private double _step4RampRate;

    [ObservableProperty] private double _step5Zone1Temp;
    [ObservableProperty] private double _step5Zone2Temp;
    [ObservableProperty] private double _step5SoakTime;
    [ObservableProperty] private double _step5RampRate;

    // ?? Global Process Settings (D321�D324) ??
    [ObservableProperty] private double _processZone1Safety;
    [ObservableProperty] private double _processZone2Safety;
    [ObservableProperty] private double _processBlower1;
    [ObservableProperty] private double _processBlower2;

    [ObservableProperty] private int? _currentProcessStep;
    [ObservableProperty] private bool _isProcessRunning;
    [ObservableProperty] private bool _isManualMode;

    // Cache for assembling 32-bit float pairs from consecutive 16-bit registers
    private readonly Dictionary<int, ushort> _plcWordCache = [];
    private readonly Dictionary<int, ushort> _energyWordCache = [];

    /// <summary>
    /// Stores rawValue for the given address and attempts to assemble a 32-bit IEEE 754
    /// float from the pair (startAddr, startAddr+1). Returns the float when both words are
    /// available, otherwise null.
    /// Set lowWordFirst to false when the first register is the high word.
    /// Set swapBytesInWords to true when each 16-bit register is byte-swapped.
    /// </summary>
    private float? TryReadFloat32(int address, ushort rawValue, int startAddr, bool lowWordFirst = true, bool swapBytesInWords = false)
    {
        _plcWordCache[address] = rawValue;
        if (_plcWordCache.TryGetValue(startAddr, out var firstWord) &&
            _plcWordCache.TryGetValue(startAddr + 1, out var secondWord))
        {
            if (swapBytesInWords)
            {
                firstWord = SwapBytes(firstWord);
                secondWord = SwapBytes(secondWord);
            }

            ushort lowWord = lowWordFirst ? firstWord : secondWord;
            ushort highWord = lowWordFirst ? secondWord : firstWord;
            uint bits = ((uint)highWord << 16) | lowWord;
            return BitConverter.Int32BitsToSingle((int)bits);
        }
        return null;
    }

    private static ushort SwapBytes(ushort value)
        => (ushort)((value >> 8) | (value << 8));

    public bool IsEnergyRegisterAddress(int address)
        // Updated ranges to match new MFM/modbus register map provided by the meter vendor
        // Total-block & phase measurements now live in D138..D171, plus the existing
        // zone blocks at D200..D210 and D250..D260.
        => address is >= 138 and <= 171 or >= 200 and <= 210 or >= 250 and <= 260;

    /// <summary>
    /// Assembles an IEEE 754 float from 3 consecutive D-registers using the Multispan MFM
    /// byte-packing format: each float's 4 data bytes are stored starting at the LOW byte
    /// of r0, spanning both bytes of r1, and ending at the HIGH byte of r2.
    ///
    /// Layout in PLC memory (CDAB order):
    ///   r0  = [XX ][C  ]   high byte ignored, low byte = C
    ///   r1  = [D  ][A  ]   high byte = D, low byte = A
    ///   r2  = [B  ][XX ]   high byte = B, low byte ignored
    ///
    /// True IEEE 754 big-endian = [A, B, C, D].
    /// Example: r0=0x044C, r1=0xCD43, r2=0x6700  ?  231.3 V
    /// </summary>
    private static float AssembleEnergyFloat(ushort r0, ushort r1, ushort r2)
    {
        byte c = (byte)(r0 & 0xFF);           // low byte of r0
        byte d = (byte)((r1 >> 8) & 0xFF);    // high byte of r1
        byte a = (byte)(r1 & 0xFF);           // low byte of r1
        byte b = (byte)((r2 >> 8) & 0xFF);    // high byte of r2
        // BitConverter on little-endian x86 needs bytes in reverse of big-endian: [D, C, B, A]
        return BitConverter.ToSingle([d, c, b, a], 0);
    }

    private float? TryReadEnergyFloat(int startAddr)
    {
        if (_energyWordCache.TryGetValue(startAddr, out var r0) &&
            _energyWordCache.TryGetValue(startAddr + 1, out var r1) &&
            _energyWordCache.TryGetValue(startAddr + 2, out var r2))
            return AssembleEnergyFloat(r0, r1, r2);
        return null;
    }

    public void UpdateEnergyRegisterValue(int address, ushort rawValue)
    {
        _energyWordCache[address] = rawValue;

        // Total KW remapped as Float32 (2-word, little-endian + byte-swap)
        if (address is 170 or 171) { var v = TryReadFloat32(address, rawValue, 170, lowWordFirst: true, swapBytesInWords: true); if (v.HasValue) TotalPowerKw = v.Value; }

        // Each float spans 3 registers: [base], [base+1], [base+2].
        // Stride between consecutive floats = 2 registers, so adjacent floats share one register.
        // Trigger re-assembly for every float whose 3-register window includes this address.
        // New total/block mapping (bases spaced per new map):
        if (address is >= 142 and <= 144) { var v = TryReadEnergyFloat(142); if (v.HasValue) VoltageR = v.Value; }
        if (address is >= 146 and <= 148) { var v = TryReadEnergyFloat(146); if (v.HasValue) VoltageY = v.Value; }
        if (address is >= 150 and <= 152) { var v = TryReadEnergyFloat(150); if (v.HasValue) VoltageB = v.Value; }

        // Total phase currents remapped as Float32 (2-word, little-endian + byte-swap)
        if (address is 158 or 159) { var v = TryReadFloat32(address, rawValue, 158, lowWordFirst: true, swapBytesInWords: true); if (v.HasValue) CurrentR = v.Value; }
        if (address is 162 or 163) { var v = TryReadFloat32(address, rawValue, 162, lowWordFirst: true, swapBytesInWords: true); if (v.HasValue) CurrentY = v.Value; }
        if (address is 166 or 167) { var v = TryReadFloat32(address, rawValue, 166, lowWordFirst: true, swapBytesInWords: true); if (v.HasValue) CurrentB = v.Value; }

        if (address is >= 200 and <= 202) { var v = TryReadEnergyFloat(200); if (v.HasValue) Zone1CurrentR = v.Value; }
        if (address is >= 204 and <= 206) { var v = TryReadEnergyFloat(204); if (v.HasValue) Zone1CurrentY = v.Value; }
        if (address is >= 208 and <= 210) { var v = TryReadEnergyFloat(208); if (v.HasValue) Zone1CurrentB = v.Value; }
        if (address is >= 250 and <= 252) { var v = TryReadEnergyFloat(250); if (v.HasValue) Zone2CurrentR = v.Value; }
        if (address is >= 254 and <= 256) { var v = TryReadEnergyFloat(254); if (v.HasValue) Zone2CurrentY = v.Value; }
        // The meter was re-mapped: Zone-2 B-phase current now appears at D164..D165 in the
        // new sheet. Still retain the older 258..260 mapping in case the device uses the
        // previous layout on some installations.
        if (address is >= 164 and <= 166) { var v = TryReadEnergyFloat(164); if (v.HasValue) Zone2CurrentB = v.Value; }
        if (address is >= 258 and <= 260) { var v = TryReadEnergyFloat(258); if (v.HasValue) Zone2CurrentB = v.Value; }
    }

    /// <summary>
    /// Assembles a 32-bit unsigned integer from a pair of consecutive 16-bit D-registers.
    /// Set lowWordFirst to false when the PLC/device exposes the first register as the high word.
    /// </summary>
    private uint? TryReadInt32(int address, ushort rawValue, int startAddr, bool lowWordFirst = true)
    {
        _plcWordCache[address] = rawValue;
        if (_plcWordCache.TryGetValue(startAddr, out var firstWord) &&
            _plcWordCache.TryGetValue(startAddr + 1, out var secondWord))
        {
            return lowWordFirst
                ? ((uint)secondWord << 16) | firstWord
                : ((uint)firstWord << 16) | secondWord;
        }
        return null;
    }

    public string CurrentProcessStepDisplay => CurrentProcessStep?.ToString() ?? "--";

    partial void OnCurrentProcessStepChanged(int? value)
    {
        OnPropertyChanged(nameof(CurrentProcessStepDisplay));
    }

    // ?? I/O Points Collection (for the I/O list page � all from PLC, Slave 4) ??
    public ObservableCollection<PlcIoPoint> IoPoints { get; } = [];

    // ?? Slave Configurations ??
    public ObservableCollection<SlaveDeviceConfig> SlaveConfigs { get; } = [];
    public int Zone1SafetyTemperatureAddress { get; }
    public int Zone2SafetyTemperatureAddress { get; }

    public PlcDataStore(ApplicationConfiguration configuration)
    {
        Zone1SafetyTemperatureAddress = configuration.PlcRegisters.Zone1SafetyTemperatureAddress;
        Zone2SafetyTemperatureAddress = configuration.PlcRegisters.Zone2SafetyTemperatureAddress;
        InitializeIoPoints();
        InitializeSlaveConfigs();
    }

    // ???????????????????????????????????????????????????????????????????
    //  I/O Points � all from PLC (Slave 4, Modbus TCP)
    // ???????????????????????????????????????????????????????????????????

    private void InitializeIoPoints()
    {
        IoPoints.Add(new PlcIoPoint { SerialNumber = 1, SlaveId = 4, Address = 10000, Name = "Single Phase Preventer", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 2, SlaveId = 4, Address = 10001, Name = "Emergency Switch", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 3, SlaveId = 4, Address = 10002, Name = "Door Limit Switch Close", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 4, SlaveId = 4, Address = 10003, Name = "Electrical Blower motor-1", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 5, SlaveId = 4, Address = 10005, Name = "Electrical Exhaust Blower Motor", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 6, SlaveId = 4, Address = 10004, Name = "Electrical Blower motor-2", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 7, SlaveId = 4, Address = 10006, Name = "VFD-1 Rotary Motor", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 8, SlaveId = 4, Address = 10007, Name = "VFD-1 Trolley Motor", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 9, SlaveId = 4, Address = 10008, Name = "Door Limit Switch Open", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 10, SlaveId = 4, Address = 10009, Name = "Trolley IN", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 11, SlaveId = 4, Address = 10010, Name = "Trolley OUT", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 12, SlaveId = 4, Address = 10011, Name = "Zone-1 Temp. Safety", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 13, SlaveId = 4, Address = 10016, Name = "Zone-2 Temp. Safety", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 14, SlaveId = 4, Address = 10017, Name = "Blower-1 Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 15, SlaveId = 4, Address = 10018, Name = "Blower-2 Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 16, SlaveId = 4, Address = 10019, Name = "Heater-1 Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 17, SlaveId = 4, Address = 10020, Name = "Heater-2 Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 18, SlaveId = 4, Address = 10021, Name = "Exhaust Blower motor Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 19, SlaveId = 4, Address = 10022, Name = "Rotary Motor Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 20, SlaveId = 4, Address = 10023, Name = "Trolley Motor Cont. ON", Type = IoType.DigitalInput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 20, SlaveId = 4, Address = 10024, Name = "MANUAL/AUTO", Type = IoType.DigitalInput });



        // ?? Digital Outputs (6 points) � PLC coil addresses 100�105 ??
        IoPoints.Add(new PlcIoPoint { SerialNumber = 1, SlaveId = 4, Address = 1600, Name = "Collection fault", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 2, SlaveId = 4, Address = 1601, Name = "Heater-1 Cont.", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 3, SlaveId = 4, Address = 1602, Name = "Heater-2 Cont.", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 4, SlaveId = 4, Address = 1603, Name = "Blower motor-1 Cont.", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 5, SlaveId = 4, Address = 1604, Name = "Blower motor-2 Cont.", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 6, SlaveId = 4, Address = 1605, Name = "Exhaust Blower Motor", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 7, SlaveId = 4, Address = 1606, Name = "TOWER RED", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 8, SlaveId = 4, Address = 1607, Name = "TOWER GREEN", Type = IoType.DigitalOutput });
        IoPoints.Add(new PlcIoPoint { SerialNumber = 9, SlaveId = 4, Address = 1617, Name = "TOWER YELLOW", Type = IoType.DigitalOutput });

    }

    // ???????????????????????????????????????????????????????????????????
    //  Slave Configs � each register/coil listed individually
    // ???????????????????????????????????????????????????????????????????

    private void InitializeSlaveConfigs()
    {


        // ?? Slave 4: PLC (Omron FINS TCP) � all digital I/O ??
        SlaveConfigs.Add(new SlaveDeviceConfig
        {
            SlaveId = 4,
            Name = "PLC",
            ConnectionType = ModbusConnectionType.OmronFinsTcp,
            IpAddress = "192.168.250.1",
            TcpPort = 9600,


            PollIntervalMs = 1000,
            Coils =
            [
                // Digital Inputs (PLC input coils)
                 new(10000,  "Single Phase Preventer"),
                 new(10001,  "Emergency Switch"),
                 new(10002, "Door Limit Switch Close"),  // W0.2
                 new(10003,  "Electrical Blower motor-1"),
                 new(10004,  "Electrical Blower motor-2"),
                 new(10006,  "VFD-1 Rotary Motor"),
                 new(10007,  "VFD-1 Trolley Motor"),
                 new(10008,  "Door Limit Switch Open"),
                 new(10009,  "Trolley IN"),
                 new(10010,  "Trolley OUT"),
                 new(10011, "Zone-1 Temp. Safety"),
                 new(10016, "Zone-2 Temp. Safety"),
                 new(10017, "Blower-1 Cont. ON"),
                 new(10018, "Blower-2 Cont. ON"),
                 new(10019, "Heater-1 Cont. ON"),
                 new(10020, "Heater-2 Cont. ON"),
                 new(10021, "Exhaust Blower motor Cont. ON"),
                 new(10022, "Rotary Motor Cont. ON"),
                 new(10023, "Trolley Motor Cont. ON"),
                 new(10024, "Manual/Auto"),
                 new(10005, "Electrical Exhaust Blower Motor"),

                // Digital Outputs (PLC output coils)
                new(1600, "Collection fault"),
                new(1601, "Heater-1 Cont."),
                new(1602, "Heater-2 Cont."),
                new(1603, "Blower motor-1 Cont."),
                new(1604, "Blower motor-2 Cont."),
                new(1605, "Exhaust Blower Motor"),
                new(1606, "TOWER RED"),
                new(1607, "TOWER GREEN"),
                new(1608, "TOWER YELLOW"),

                // W44.0 remains command-related; W110.0 is the actual running status feedback.
                new(10704, "W44.0 - Process Run/Stop"),
                new(11760, "W110.0 - Process Running Feedback"),
                // Note: W area (10640�10643) and coils 400�407 are write-only pulses � not polled
            ],
            HoldingRegisters =
            [
                new(100, "D100 - Zone 1 setpoint Temperature"),
                new(101, "D101 - Zone 2 setpoint Temperature"),
                new(102, "D102 - Zone 1 SV"),
                new(103, "D103 - Zone 2 SV"),
                new(120,"D120 - Zone 1 Actual Temperature pv "),
                new(121,"D121 - Zone 2 Actual Temperature pv"),
                new(126, "D126 - R-Phase Voltage LSW"),
                new(127, "D127 - R-Phase Voltage MSW"),
                new(128, "D128 - Y-Phase Voltage LSW"),
                new(129, "D129 - Y-Phase Voltage MSW"),
                new(130, "D130 - B-Phase Voltage LSW"),
                new(131, "D131 - B-Phase Voltage MSW"),
                new(132, "D132 - R-Phase Current LSW"),
                new(133, "D133 - R-Phase Current MSW"),
                new(134, "D134 - Y-Phase Current LSW"),
                new(135, "D135 - Y-Phase Current MSW"),
                new(136, "D136 - B-Phase Current LSW"),
                new(137, "D137 - B-Phase Current MSW"),
                new(138, "D138 - Total KW LSW"),
                new(139, "D139 - Total KW MSW"),
                new(200, "D200 - Zone1 R-Phase Current LSW"),
                new(201, "D201 - Zone1 R-Phase Current MSW"),
                new(202, "D202 - Zone1 R-Phase Current (3rd word)"),
                new(204, "D204 - Zone1 Y-Phase Current LSW"),
                new(205, "D205 - Zone1 Y-Phase Current MSW"),
                new(206, "D206 - Zone1 Y-Phase Current (3rd word)"),
                new(208, "D208 - Zone1 B-Phase Current LSW"),
                new(209, "D209 - Zone1 B-Phase Current MSW"),
                new(210, "D210 - Zone1 B-Phase Current (3rd word)"),
                new(250, "D250 - Zone2 R-Phase Current LSW"),
                new(251, "D251 - Zone2 R-Phase Current MSW"),
                new(252, "D252 - Zone2 R-Phase Current (3rd word)"),
                new(254, "D254 - Zone2 Y-Phase Current LSW"),
                new(255, "D255 - Zone2 Y-Phase Current MSW"),
                new(256, "D256 - Zone2 Y-Phase Current (3rd word)"),
                new(258, "D258 - Zone2 B-Phase Current LSW"),
                new(259, "D259 - Zone2 B-Phase Current MSW"),
                new(260, "D260 - Zone2 B-Phase Current (3rd word)"),
                new(301, "D301 - Step 1 Zone 1 Temp"),
                new(302, "D302 - Step 1 Zone 2 Temp"),
                new(303, "D303 - Step 1 Soak Time"),
                new(304, "D304 - Step 1 Ramp Rate"),
                new(305, "D305 - Step 2 Zone 1 Temp"),
                new(306, "D306 - Step 2 Zone 2 Temp"),
                new(307, "D307 - Step 2 Soak Time"),
                new(308, "D308 - Step 2 Ramp Rate"),
                new(309, "D309 - Step 3 Zone 1 Temp"),
                new(310, "D310 - Step 3 Zone 2 Temp"),
                new(311, "D311 - Step 3 Soak Time"),
                new(312, "D312 - Step 3 Ramp Rate"),
                new(313, "D313 - Step 4 Zone 1 Temp"),
                new(314, "D314 - Step 4 Zone 2 Temp"),
                new(315, "D315 - Step 4 Soak Time"),
                new(316, "D316 - Step 4 Ramp Rate"),
                new(317, "D317 - Step 5 Zone 1 Temp"),
                new(318, "D318 - Step 5 Zone 2 Temp"),
                new(319, "D319 - Step 5 Soak Time"),
                new(320, "D320 - Step 5 Ramp Rate"),
                new(Zone1SafetyTemperatureAddress, "Configured Zone 1 Safety Temperature"),
                new(Zone2SafetyTemperatureAddress, "Configured Zone 2 Safety Temperature"),
                new(323, "D323 - Blower 1"),
                new(324, "D324 - Blower 2"),
                new(325, "D325 - zone1 temp setpoint"),
                new(326, "D326 - zone2 temp setpoint"),
                new(327, "D327 - zone1 safety temperature"),
                new(328, "D328 - zone2 safety temperature"),
                new(123, "D123 - Zone1 Job Temperature"),
                new(125, "D125 - Zone2 Job Temperature"),
                new(400, "D400 - Current Process Step"),
               // new(400, "D400 - Zone 1 Temp Setpoint"),
                new(401, "D401 - Elapsed time"),
                new(402, "D402 - Set Time"),
                // Updated energy/meter register map (per latest sheet):
                new(170, "D170 - Total KW LSW"),
                new(171, "D171 - Total KW MSW"),
                new(142, "D142 - R-Phase Voltage LSW"),
                new(143, "D143 - R-Phase Voltage MSW"),
                new(144, "D144 - R-Phase Voltage (3rd word)"),
                new(146, "D146 - Y-Phase Voltage LSW"),
                new(147, "D147 - Y-Phase Voltage MSW"),
                new(148, "D148 - Y-Phase Voltage (3rd word)"),
                new(150, "D150 - B-Phase Voltage LSW"),
                new(151, "D151 - B-Phase Voltage MSW"),
                new(152, "D152 - B-Phase Voltage (3rd word)"),
                new(158, "D158 - Total R-Phase Current LSW"),
                new(159, "D159 - Total R-Phase Current MSW"),
                new(162, "D162 - Total Y-Phase Current LSW"),
                new(163, "D163 - Total Y-Phase Current MSW"),
                new(166, "D166 - Total B-Phase Current LSW"),
                new(167, "D167 - Total B-Phase Current MSW"),
                // Zone-2 B-phase (moved in new sheet)
                new(164, "D164 - Zone2 B-Phase Current LSW"),
                new(165, "D165 - Zone2 B-Phase Current MSW"),
                new(166, "D166 - Zone2 B-Phase Current (3rd word)"),
            ],
        });
    }

    // ???????????????????????????????????????????????????????????????????
    //  Register Mapping � called per-address from the polling service
    // ???????????????????????????????????????????????????????????????????

    /// <summary>
    /// Maps a single holding register value from a slave into the correct typed property.
    /// Called on the UI dispatcher thread.
    /// </summary>
    public void UpdateRegisterValue(int slaveId, int address, ushort rawValue)
    {
        switch (slaveId)
        {


            case 4: // PLC (Omron FINS TCP) � D register values
                switch (address)
                {
                    case 120: Zone1Temperature = rawValue/10.0; break; // D120 � Zone 1 PV
                    case 100: Zone1Setpoint = rawValue/10.0; break; // D100 � Zone 1 Temperature (PV)
                    case 121: Zone2Temperature = rawValue/10.0; break; // D121 � Zone 2 PV
                    case 123: Zone1Output = rawValue/10.0; break; // D123 � Zone 1 Job PV, tenths-scaled like D120/D121/D100
                    case 125: Zone2Output = rawValue/10.0; break; // D125 � Zone 2 Job PV, tenths-scaled like D123
                    //case 102: Zone1Setpoint = rawValue; break; // D102 � Zone 1 SV (live setpoint from controller)
                    case 103: Zone2Setpoint = rawValue; break; // D103 � Zone 2 SV (live setpoint from controller)
                    case 301: Step1Zone1Temp = rawValue; break;
                    case 302: Step1Zone2Temp = rawValue; break;
                    case 303: Step1SoakTime = rawValue; break;
                    case 304: Step1RampRate = rawValue; break;
                    case 305: Step2Zone1Temp = rawValue; break;
                    case 306: Step2Zone2Temp = rawValue; break;
                    case 307: Step2SoakTime = rawValue; break;
                    case 308: Step2RampRate = rawValue; break;
                    case 309: Step3Zone1Temp = rawValue; break;
                    case 310: Step3Zone2Temp = rawValue; break;
                    case 311: Step3SoakTime = rawValue; break;
                    case 312: Step3RampRate = rawValue; break;
                    case 313: Step4Zone1Temp = rawValue; break;
                    case 314: Step4Zone2Temp = rawValue; break;
                    case 315: Step4SoakTime = rawValue; break;
                    case 316: Step4RampRate = rawValue; break;
                    case 317: Step5Zone1Temp = rawValue; break;
                    case 318: Step5Zone2Temp = rawValue; break;
                    case 319: Step5SoakTime = rawValue; break;
                    case 320: Step5RampRate = rawValue; break;
                    case var configuredAddress when configuredAddress == Zone1SafetyTemperatureAddress: ProcessZone1Safety = rawValue; break;
                    case var configuredAddress when configuredAddress == Zone2SafetyTemperatureAddress: ProcessZone2Safety = rawValue; break;
                    case 323: ProcessBlower1 = rawValue; break;
                    case 324: ProcessBlower2 = rawValue; break;
                    case 325: Zone1SetPointValueManual = rawValue/10.0; break; // D325 � Zone 1 Manual Setpoint, tenths-scaled like D100
                    case 326: Zone2Setpoint = rawValue; break; // D326 � Zone 2 Temp Setpoint (plain 16-bit int)
                    case 327: Zone1SafetyTemperature = rawValue; break;
                    case 328: Zone2SafetyTemperature = rawValue; break;
                    //case 123: Zone1JobTemperature = rawValue; break; // D123 � Zone1 job thermocouple PV
                    //case 125: Zone2JobTemperature = rawValue; break; // D125 � Zone2 job thermocouple PV
                    case 400: CurrentProcessStep = rawValue == 0 ? (int?)null : (int)rawValue; break; // D400
                    case 401: ElapsedTime = rawValue; break; // D401 � Elapsed Time (plain int, minutes)
                    case 402: SoakTime = rawValue; break;

                    // MFM/Meter registers � handled primarily via UpdateEnergyRegisterValue.
                    // Fallback groups (each float spans a 3-register window in the meter packing)
                    // Accept older meter placement (D126..D137) as well as the newer map so
                    // the app assembles the floats regardless of which mapping the PLC/meter uses.
                    case 126:
                    case 127:
                    case 128:
                    case 129:
                    case 130:
                    case 131:
                    case 132:
                    case 133:
                    case 134:
                    case 135:
                    case 136:
                    case 137:
                        { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 138: case 139: case 140: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 142: case 143: case 144: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 146: case 147: case 148: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 150: case 151: case 152: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 154: case 155: case 156: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 158: case 159: case 160: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 162: case 163: case 164: case 165: case 166: case 167: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 170: case 171: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 200: case 201: case 202: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 204: case 205: case 206: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 208: case 209: case 210: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 250: case 251: case 252: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 254: case 255: case 256: { UpdateEnergyRegisterValue(address, rawValue); break; }
                    case 258: case 259: case 260: { UpdateEnergyRegisterValue(address, rawValue); break; }
                }
                break;
        }
    }

    /// <summary>
    /// Maps a single coil value from a slave into the matching IoPoint.
    /// The IoPoint.BoolValue update triggers the I/O page status circle
    /// and stamps TimeLastChanged automatically.
    /// Called on the UI dispatcher thread.
    /// </summary>
    public void UpdateCoilValue(int slaveId, int address, bool value)
    {
        var point = IoPoints.FirstOrDefault(p => p.SlaveId == slaveId && p.Address == address);
        if (point is not null)
            point.BoolValue = value;

        if (slaveId != 4)
            return;

        switch (address)
        {
            // Contactor feedback coils � drive status indicators on Manual page
            case 101: Heater1ManualStatus = value; break; // CIO100.1 � Heater-1 Cont.
            case 102: Heater2ManualStatus = value; break; // CIO100.2 � Heater-2 Cont.
            case 103: Blower1ManualStatus = value; break; // CIO100.3 � Blower motor-1 Cont.
            case 104: Blower2ManualStatus = value; break; // CIO100.4 � Blower motor-2 Cont.

            // W110.0 � actual machine cycle running feedback for UI sync
            // MODIFIED
            case 10704:
                IsProcessRunning = value;
                break;

            // OLD - kept for reference
            // case 11760:
            //     DiagnosticLogger.Instance.Log("PLC-COIL", $"UpdateCoilValue address=11760 (W110.0) incoming={value}, previous={IsProcessRunning}");
            //     IsProcessRunning = value;
            //     DiagnosticLogger.Instance.Log("PLC-COIL", $"IsProcessRunning updated={IsProcessRunning}");
            //     break;
            // MODIFIED
            case 11760:
                // W110.0 is retained for monitoring/diagnostic purposes.
                // It must NOT overwrite IsProcessRunning.
                break;
            // MANUAL/AUTO selector input (coil 10024)
            case 10024:
                DiagnosticLogger.Instance.Log("PLC-COIL", $"UpdateCoilValue address=10024 (W1.8) incoming={value}, previous={IsManualMode}");
                IsManualMode = value;
                DiagnosticLogger.Instance.Log("PLC-COIL", $"IsManualMode updated={IsManualMode}");
                break;
            // W40 write-back (optimistic update after coil write)
            case 10640: Blower1ManualStatus = value; break;
            case 10641: Blower2ManualStatus = value; break;
            case 10642: Heater1ManualStatus = value; break;
            case 10643: Heater2ManualStatus = value; break;
        }
    }
}
