using System.IO.Ports;
using System.Net.Sockets;
using TempControl.Models;

namespace TempControl.Services;

public sealed class BasicModbusTransportFactory : IModbusTransportFactory
{
    public IModbusTransport CreateTransport(SlaveDeviceConfig config) => config.ConnectionType switch
    {
        ModbusConnectionType.OmronFinsTcp => new OmronFinsTcpTransport(config),
        ModbusConnectionType.Tcp          => new TcpModbusTransport(config),
        _                                 => new RtuModbusTransport(config)
    };
}

internal abstract class BasicModbusTransport : IModbusTransport
{
    public bool IsConnected { get; protected set; }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task DisconnectAsync();
    public abstract Task<ushort[]> ReadHoldingRegistersAsync(int slaveId, int startAddress, int count, CancellationToken ct = default);
    public abstract Task<bool[]> ReadCoilsAsync(int slaveId, int startAddress, int count, CancellationToken ct = default);
    public abstract Task WriteSingleRegisterAsync(int slaveId, int address, ushort value, CancellationToken ct = default);
    public abstract Task WriteSingleCoilAsync(int slaveId, int address, bool value, CancellationToken ct = default);
    public abstract void Dispose();
}

internal sealed class TcpModbusTransport : BasicModbusTransport
{
    private readonly SlaveDeviceConfig _config;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;

    public TcpModbusTransport(SlaveDeviceConfig config)
    {
        _config = config;
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _client = new TcpClient();
        await _client.ConnectAsync(_config.IpAddress, _config.TcpPort, ct);
        _stream = _client.GetStream();
        IsConnected = true;
    }

    public override Task DisconnectAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public override async Task<ushort[]> ReadHoldingRegistersAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x03, (ushort)startAddress, (ushort)count);
        var response = await SendAndReceiveAsync(request, count * 2 + 9, ct);
        ValidateTcpFunction(response, 0x03);

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (ushort)((response[9 + (i * 2)] << 8) | response[10 + (i * 2)]);
        }

        return values;
    }

    public override async Task<bool[]> ReadCoilsAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x01, (ushort)startAddress, (ushort)count);
        var bytesNeeded = (count + 7) / 8;
        var response = await SendAndReceiveAsync(request, bytesNeeded + 9, ct);
        ValidateTcpFunction(response, 0x01);

        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var responseByte = response[9 + (i / 8)];
            values[i] = ((responseByte >> (i % 8)) & 0x01) == 0x01;
        }

        return values;
    }

    public override async Task WriteSingleRegisterAsync(int slaveId, int address, ushort value, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x06, (ushort)address, value);
        var response = await SendAndReceiveAsync(request, 12, ct);
        ValidateTcpFunction(response, 0x06);
    }

    public override async Task WriteSingleCoilAsync(int slaveId, int address, bool value, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x05, (ushort)address, value ? (ushort)0xFF00 : (ushort)0x0000);
        var response = await SendAndReceiveAsync(request, 12, ct);
        ValidateTcpFunction(response, 0x05);
    }

    public override void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        IsConnected = false;
    }

    private byte[] CreateRequest(byte slaveId, byte function, ushort address, ushort valueOrCount)
    {
        _transactionId++;
        return
        [
            (byte)(_transactionId >> 8),
            (byte)(_transactionId & 0xFF),
            0x00,
            0x00,
            0x00,
            0x06,
            slaveId,
            function,
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(valueOrCount >> 8),
            (byte)(valueOrCount & 0xFF)
        ];
    }

    private async Task<byte[]> SendAndReceiveAsync(byte[] request, int minimumResponseLength, CancellationToken ct)
    {
        if (_stream is null)
            throw new InvalidOperationException("TCP transport is not connected.");

        await _stream.WriteAsync(request, ct);

        var buffer = new byte[Math.Max(minimumResponseLength, 260)];
        var bytesRead = await _stream.ReadAsync(buffer, ct);
        if (bytesRead == 0)
            throw new InvalidOperationException("No Modbus TCP response received.");

        return buffer[..bytesRead];
    }

    private static void ValidateTcpFunction(byte[] response, byte expectedFunction)
    {
        if (response.Length < 9)
            throw new InvalidOperationException("Modbus TCP response too short.");

        var function = response[7];
        if (function == (expectedFunction | 0x80))
            throw new InvalidOperationException($"Modbus exception code: {response[8]}");

        if (function != expectedFunction)
            throw new InvalidOperationException($"Unexpected Modbus function code: {function}");
    }
}

internal sealed class RtuModbusTransport : BasicModbusTransport
{
    private readonly SlaveDeviceConfig _config;
    private SerialPort? _serialPort;

    public RtuModbusTransport(SlaveDeviceConfig config)
    {
        _config = config;
    }

    public override Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return Task.CompletedTask;

        _serialPort = new SerialPort(
            _config.PortName,
            _config.BaudRate,
            ParseParity(_config.Parity),
            _config.DataBits,
            ParseStopBits(_config.StopBits))
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        _serialPort.Open();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public override Task DisconnectAsync()
    {
        if (_serialPort is not null)
        {
            _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
        }

        IsConnected = false;
        return Task.CompletedTask;
    }

    public override async Task<ushort[]> ReadHoldingRegistersAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x03, (ushort)startAddress, (ushort)count);
        var response = await SendAndReceiveAsync(request, count * 2 + 5, ct);
        ValidateRtuFunction(response, 0x03);

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (ushort)((response[3 + (i * 2)] << 8) | response[4 + (i * 2)]);
        }

        return values;
    }

    public override async Task<bool[]> ReadCoilsAsync(int slaveId, int startAddress, int count, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x01, (ushort)startAddress, (ushort)count);
        var response = await SendAndReceiveAsync(request, ((count + 7) / 8) + 5, ct);
        ValidateRtuFunction(response, 0x01);

        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var responseByte = response[3 + (i / 8)];
            values[i] = ((responseByte >> (i % 8)) & 0x01) == 0x01;
        }

        return values;
    }

    public override async Task WriteSingleRegisterAsync(int slaveId, int address, ushort value, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x06, (ushort)address, value);
        var response = await SendAndReceiveAsync(request, 8, ct);
        ValidateRtuFunction(response, 0x06);
    }

    public override async Task WriteSingleCoilAsync(int slaveId, int address, bool value, CancellationToken ct = default)
    {
        var request = CreateRequest((byte)slaveId, 0x05, (ushort)address, value ? (ushort)0xFF00 : (ushort)0x0000);
        var response = await SendAndReceiveAsync(request, 8, ct);
        ValidateRtuFunction(response, 0x05);
    }

    public override void Dispose()
    {
        if (_serialPort is not null)
        {
            _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
        }

        IsConnected = false;
    }

    private static Parity ParseParity(string parity)
    {
        return Enum.TryParse<Parity>(parity, true, out var result) ? result : Parity.None;
    }

    private static StopBits ParseStopBits(int stopBits)
    {
        return stopBits switch
        {
            0 => StopBits.None,
            2 => StopBits.Two,
            3 => StopBits.OnePointFive,
            _ => StopBits.One,
        };
    }

    private static byte[] CreateRequest(byte slaveId, byte function, ushort address, ushort valueOrCount)
    {
        var frame = new byte[8];
        frame[0] = slaveId;
        frame[1] = function;
        frame[2] = (byte)(address >> 8);
        frame[3] = (byte)(address & 0xFF);
        frame[4] = (byte)(valueOrCount >> 8);
        frame[5] = (byte)(valueOrCount & 0xFF);

        var crc = ComputeCrc(frame, 6);
        frame[6] = (byte)(crc & 0xFF);
        frame[7] = (byte)(crc >> 8);
        return frame;
    }

    private async Task<byte[]> SendAndReceiveAsync(byte[] request, int minimumResponseLength, CancellationToken ct)
    {
        if (_serialPort is null)
            throw new InvalidOperationException("RTU transport is not connected.");

        _serialPort.DiscardInBuffer();
        await _serialPort.BaseStream.WriteAsync(request, ct);
        await _serialPort.BaseStream.FlushAsync(ct);

        var buffer = new byte[Math.Max(minimumResponseLength, 256)];
        var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, ct);
        if (bytesRead == 0)
            throw new InvalidOperationException("No Modbus RTU response received.");

        return buffer[..bytesRead];
    }

    private static void ValidateRtuFunction(byte[] response, byte expectedFunction)
    {
        if (response.Length < 5)
            throw new InvalidOperationException("Modbus RTU response too short.");

        var function = response[1];
        if (function == (expectedFunction | 0x80))
            throw new InvalidOperationException($"Modbus RTU exception code: {response[2]}");

        if (function != expectedFunction)
            throw new InvalidOperationException($"Unexpected RTU function code: {function}");
    }

    private static ushort ComputeCrc(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (var pos = 0; pos < length; pos++)
        {
            crc ^= data[pos];
            for (var i = 0; i < 8; i++)
            {
                var lsbSet = (crc & 0x0001) != 0;
                crc >>= 1;
                if (lsbSet)
                    crc ^= 0xA001;
            }
        }

        return crc;
    }
}
