using System;
using System.IO;
using System.Linq;
using HidSharp;

namespace WarthogLedControl;

public sealed class WarthogThrottle : IDisposable
{
    private const int VendorId = 0x044F;
    private const int ProductId = 0x0404;

    private const byte Command = 0x01;
    private const byte LedCommand = 0x06;

    private static readonly byte[] StatusLedBits =
    {
        1 << 2,
        1 << 1,
        1 << 4,
        1 << 0,
        1 << 6
    };

    private const byte BacklightBit = 1 << 3;

    private HidDevice? _device;
    private byte _ledMask;
    private byte _brightness = 1;

    public event Action<string>? LogMessage;

    private void Log(string message)
    {
        LogMessage?.Invoke(message);
    }

    public bool IsConnected => _device is not null;

    public string DeviceDescription
    {
        get
        {
            if (_device is null)
                return "Nicht verbunden";

            try
            {
                return _device.GetProductName();
            }
            catch
            {
                return $"VID 0x{VendorId:X4}, PID 0x{ProductId:X4}";
            }
        }
    }

    public bool FindDevice()
    {
        Log("Suche Warthog Throttle...");

        _device = DeviceList.Local
            .GetHidDevices(VendorId, ProductId)
            .FirstOrDefault();

        if (_device is null)
        {
            Log("Warthog Throttle NICHT gefunden.");
            return false;
        }

        Log($"Warthog Throttle gefunden: {DeviceDescription}");
        Log($"VID=0x{VendorId:X4}, PID=0x{ProductId:X4}");

        return true;
    }

    public void SetBacklightLevel(int level)
    {
        Log($"SetBacklightLevel({level})");

        EnsureDevice();

        level = Math.Clamp(level, 0, 3);

        if (level == 0)
        {
            _ledMask &= unchecked((byte)~BacklightBit);
            _brightness = 0;
        }
        else
        {
            _ledMask |= BacklightBit;
            _brightness = (byte)level;
        }

        Log(
            $"Backlight: level={level}, " +
            $"mask=0x{_ledMask:X2}, " +
            $"brightness=0x{_brightness:X2}");

        Send();
    }

    public void SetBacklight(bool enabled, int brightness)
    {
        EnsureDevice();

        _brightness = (byte)Math.Clamp(brightness, 1, 3);

        if (enabled)
            _ledMask |= BacklightBit;
        else
            _ledMask &= unchecked((byte)~BacklightBit);

        Send();
    }

    public void SetStatusLed(int index, bool enabled, int brightness)
    {
        EnsureDevice();

        if (index < 0 || index >= StatusLedBits.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        _brightness = (byte)Math.Clamp(brightness, 1, 3);

        if (enabled)
            _ledMask |= StatusLedBits[index];
        else
            _ledMask &= unchecked((byte)~StatusLedBits[index]);

        Send();
    }

    public void Off()
    {
        EnsureDevice();

        _ledMask = 0;
        _brightness = 0;

        Send();
    }

    private void EnsureDevice()
    {
        if (_device is null)
        {
            Log("Kein Warthog-Handle vorhanden – FindDevice() wird ausgeführt.");
            FindDevice();
        }

        if (_device is null)
        {
            Log("FEHLER: Warthog Throttle wurde nicht gefunden.");
            throw new InvalidOperationException(
                "HOTAS Warthog Throttle wurde nicht gefunden.");
        }
    }

    private void Send()
    {
        EnsureDevice();

        byte[] buffer =
        {
            Command,
            LedCommand,
            _ledMask,
            _brightness
        };

        Log(
            $"HID SEND: " +
            $"{buffer[0]:X2} {buffer[1]:X2} " +
            $"{buffer[2]:X2} {buffer[3]:X2}");

        if (!_device!.TryOpen(out HidStream? stream) || stream is null)
        {
            Log("FEHLER: HID-Gerät konnte nicht geöffnet werden.");
            throw new IOException(
                "HID-Gerät konnte nicht geöffnet werden.");
        }

        using (stream)
        {
            if (!stream.CanWrite)
            {
                Log("FEHLER: HID-Gerät ist nicht beschreibbar.");
                throw new IOException(
                    "HID-Gerät ist nicht beschreibbar.");
            }

            Log("HID Stream geöffnet – sende Report.");

            stream.Write(buffer, 0, buffer.Length);

            Log("HID Report erfolgreich gesendet.");
        }
    }

    public void Dispose()
    {
        _device = null;
    }
}