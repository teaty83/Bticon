using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Bticon
{
    public record BluetoothDeviceInfo(
        string Name,
        ulong Address,
        string AddressHex,
        bool IsConnected,
        byte? BatteryPercentage,
        DateTime? LastUpdated,
        string? DeviceNodeId
    );

    public class BluetoothBatteryService : IDisposable
    {
        #region Win32 Native Interop (CfgMgr32)
        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out int pdnDevInst, string pDeviceID, int ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_PropertyW(
            int dnDevInst,
            ref DEVPROPKEY propertyKey,
            out int propertyType,
            IntPtr propertyBuffer,
            ref int propertyBufferSize,
            int ulFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public int pid;
        }

        private const int CR_SUCCESS = 0;
        private const int CR_BUFFER_SMALL = 0x1A;
        private const int CM_LOCATE_DEVNODE_NORMAL = 0;
        private const int DEVPROP_TYPE_BYTE = 0x00000003;
        private const int DEVPROP_TYPE_INT32 = 0x00000006;
        private const int DEVPROP_TYPE_UINT32 = 0x00000007;

        // Clave de propiedad de batería HFP de Windows: {104EA319-6EE2-4701-BD47-8DDBF425BBE5} pid 2
        private static readonly DEVPROPKEY DEVPKEY_Bluetooth_HfpBattery = new()
        {
            fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
            pid = 2
        };

        private static readonly Guid GattBatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid GattBatteryLevelUuid = new("00002a19-0000-1000-8000-00805f9b34fb");
        #endregion

        private readonly ConcurrentDictionary<ulong, string> _hfpInstanceIdCache = new();
        private readonly ConcurrentDictionary<ulong, BluetoothDevice> _activeBtDevices = new();
        private bool _disposed;

        public event Action? DevicesChanged;

        public async Task<List<BluetoothDeviceInfo>> ScanDevicesAsync()
        {
            var results = new List<BluetoothDeviceInfo>();

            try
            {
                // Buscar todos los dispositivos Bluetooth emparejados
                string aqs = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                var deviceInfos = await DeviceInformation.FindAllAsync(aqs);

                foreach (var info in deviceInfos)
                {
                    try
                    {
                        var btDev = await BluetoothDevice.FromIdAsync(info.Id);
                        if (btDev == null) continue;

                        ulong address = btDev.BluetoothAddress;
                        string addressHex = address.ToString("X12", CultureInfo.InvariantCulture);
                        string name = string.IsNullOrWhiteSpace(btDev.Name) ? info.Name : btDev.Name;
                        bool isConnected = btDev.ConnectionStatus == BluetoothConnectionStatus.Connected;

                        // Registrar evento de cambio de conexión si aún no está registrado
                        if (!_activeBtDevices.ContainsKey(address))
                        {
                            btDev.ConnectionStatusChanged += (sender, args) =>
                            {
                                DevicesChanged?.Invoke();
                            };
                            _activeBtDevices[address] = btDev;
                        }

                        byte? battery = null;
                        string? foundNodeId = null;

                        // Si está conectado, intentamos obtener la batería
                        if (isConnected)
                        {
                            // 1. Intentar por HFP (Hands-Free Profile via CfgMgr32)
                            var hfpResult = GetHfpBattery(addressHex);
                            if (hfpResult.Battery.HasValue)
                            {
                                battery = hfpResult.Battery;
                                foundNodeId = hfpResult.InstanceId;
                                _hfpInstanceIdCache[address] = hfpResult.InstanceId!;
                            }

                            // 2. Si no se obtuvo, intentar por BLE GATT Battery Service
                            if (!battery.HasValue)
                            {
                                battery = await TryGetGattBatteryLevelAsync(address);
                            }
                        }

                        results.Add(new BluetoothDeviceInfo(
                            Name: name,
                            Address: address,
                            AddressHex: addressHex,
                            IsConnected: isConnected,
                            BatteryPercentage: battery,
                            LastUpdated: battery.HasValue ? DateTime.Now : null,
                            DeviceNodeId: foundNodeId
                        ));
                    }
                    catch
                    {
                        // Continuar con el siguiente dispositivo si hay error de consulta individual
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning devices: {ex.Message}");
            }

            return results;
        }

        private (byte? Battery, string? InstanceId) GetHfpBattery(string addressHex)
        {
            try
            {
                // Si ya tenemos en caché el ID de instancia del dispositivo HFP, probamos directamente
                if (ulong.TryParse(addressHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong addr) &&
                    _hfpInstanceIdCache.TryGetValue(addr, out string? cachedId))
                {
                    var cachedLevel = ReadDevNodeBattery(cachedId);
                    if (cachedLevel.HasValue)
                    {
                        return (cachedLevel.Value, cachedId);
                    }
                }

                // Si no, exploramos el registro en BTHENUM buscando el nodo Hands-Free (111e) con la dirección MAC del dispositivo
                using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\BTHENUM");
                if (enumKey == null) return (null, null);

                foreach (var subKeyName in enumKey.GetSubKeyNames())
                {
                    if (!subKeyName.Contains("111e", StringComparison.OrdinalIgnoreCase)) continue;

                    using var serviceKey = enumKey.OpenSubKey(subKeyName);
                    if (serviceKey == null) continue;

                    foreach (var instanceName in serviceKey.GetSubKeyNames())
                    {
                        if (!instanceName.Contains(addressHex, StringComparison.OrdinalIgnoreCase)) continue;

                        string instanceId = $"BTHENUM\\{subKeyName}\\{instanceName}";
                        byte? level = ReadDevNodeBattery(instanceId);
                        if (level.HasValue)
                        {
                            return (level, instanceId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading HFP battery: {ex.Message}");
            }

            return (null, null);
        }

        private byte? ReadDevNodeBattery(string instanceId)
        {
            try
            {
                int cr = CM_Locate_DevNodeW(out int devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL);
                if (cr != CR_SUCCESS) return null;

                int bufferSize = 0;
                var propKey = DEVPKEY_Bluetooth_HfpBattery;

                cr = CM_Get_DevNode_PropertyW(devInst, ref propKey, out int propertyType, IntPtr.Zero, ref bufferSize, 0);
                if (cr != CR_BUFFER_SMALL && cr != CR_SUCCESS) return null;
                if (bufferSize <= 0) return null;

                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    cr = CM_Get_DevNode_PropertyW(devInst, ref propKey, out propertyType, buffer, ref bufferSize, 0);
                    if (cr != CR_SUCCESS) return null;

                    if (propertyType == DEVPROP_TYPE_BYTE && bufferSize >= 1)
                    {
                        return Marshal.ReadByte(buffer);
                    }

                    if ((propertyType == DEVPROP_TYPE_INT32 || propertyType == DEVPROP_TYPE_UINT32) && bufferSize >= 4)
                    {
                        int val = Marshal.ReadInt32(buffer);
                        if (val >= 0 && val <= 100) return (byte)val;
                    }

                    if (bufferSize >= 1)
                    {
                        byte b = Marshal.ReadByte(buffer);
                        if (b <= 100) return b;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                // Silencioso en caso de lectura no disponible
            }

            return null;
        }

        private async Task<byte?> TryGetGattBatteryLevelAsync(ulong address)
        {
            try
            {
                var leDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                if (leDevice == null) return null;

                var gattServices = await leDevice.GetGattServicesForUuidAsync(GattBatteryServiceUuid);
                if (gattServices.Status != GattCommunicationStatus.Success || gattServices.Services.Count == 0)
                {
                    return null;
                }

                foreach (var service in gattServices.Services)
                {
                    using (service)
                    {
                        var chars = await service.GetCharacteristicsForUuidAsync(GattBatteryLevelUuid);
                        if (chars.Status != GattCommunicationStatus.Success) continue;

                        foreach (var characteristic in chars.Characteristics)
                        {
                            var result = await characteristic.ReadValueAsync();
                            if (result.Status == GattCommunicationStatus.Success)
                            {
                                var reader = DataReader.FromBuffer(result.Value);
                                if (reader.UnconsumedBufferLength >= 1)
                                {
                                    byte level = reader.ReadByte();
                                    if (level <= 100) return level;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Muchos auriculares clásicos no implementan BLE GATT
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _activeBtDevices)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _activeBtDevices.Clear();
        }
    }
}
