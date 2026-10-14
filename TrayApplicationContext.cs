using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Bticon
{
    public class TrayApplicationContext : ApplicationContext
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetCurrentPackageFullName")]
        private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

        private const string AppRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryValueName = "Bticon_BatteryMonitor";
        private const string StartupTaskId = "BticonStartupTask";
        private const int ErrorInsufficientBuffer = 122;
        private const int AppModelErrorNoPackage = 15700;
        private const int NotifyIconMaxTooltipLength = 63;

        private readonly Control _invoker;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly BluetoothBatteryService _batteryService;
        private readonly System.Windows.Forms.Timer _refreshTimer;

        private List<BluetoothDeviceInfo> _cachedDevices = new();
        private ulong? _selectedDeviceAddress;
        private IconStyle _currentStyle = IconStyle.Number;
        private IconSize _currentIconSize = IconSize.Normal;
        private byte? _currentBatteryPercentage;
        private bool _isCurrentDeviceConnected;
        private Icon? _currentIcon;
        private IntPtr _currentHIcon = IntPtr.Zero;
        private int _refreshInProgress;

        private ToolStripMenuItem _headerMenuItem   = null!;
        private ToolStripMenuItem _devicesSubMenu   = null!;
        private ToolStripMenuItem _styleSubMenu     = null!;
        private ToolStripMenuItem _styleNumberItem  = null!;
        private ToolStripMenuItem _styleBatteryItem = null!;
        private ToolStripMenuItem _sizeSmallItem    = null!;
        private ToolStripMenuItem _sizeNormalItem   = null!;
        private ToolStripMenuItem _sizeLargeItem    = null!;
        private ToolStripMenuItem _startupItem      = null!;

        public TrayApplicationContext()
        {
            Log("=== Iniciando TrayApplicationContext ===");

            _invoker = new Control();
            _ = _invoker.Handle;

            _contextMenu = BuildContextMenu();
            _ = UpdateStartupMenuStateAsync();

            _notifyIcon = new NotifyIcon
            {
                Text = "Bticon - Batería Bluetooth",
                ContextMenuStrip = _contextMenu
            };

            _notifyIcon.DoubleClick += (s, e) => OpenBluetoothSettings();

            // Establecer icono inicial
            UpdateIcon(null, false);
            _notifyIcon.Visible = true;
            Log("NotifyIcon visible establecido.");

            _batteryService = new BluetoothBatteryService();
            _batteryService.DevicesChanged += () =>
            {
                Log("Evento DevicesChanged recibido.");
                _ = RefreshBatteryStatusAsync();
            };

            // Temporizador periódico de refresco (cada 20 segundos)
            _refreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 20000
            };
            _refreshTimer.Tick += async (s, e) => await RefreshBatteryStatusAsync();
            _refreshTimer.Start();
            Log("Temporizador de refresco iniciado.");

            _ = RefreshBatteryStatusAsync();
        }

        public static void Log(string message)
        {
            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Bticon");
                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, "bticon_debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch { }
        }

        private void RunOnUi(Action action)
        {
            if (_invoker.IsDisposed || !_invoker.IsHandleCreated) return;

            if (_invoker.InvokeRequired)
            {
                _invoker.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            _headerMenuItem = new ToolStripMenuItem("Buscando auriculares...")
            {
                Enabled = false,
                Font = new Font(menu.Font, FontStyle.Bold)
            };
            menu.Items.Add(_headerMenuItem);
            menu.Items.Add(new ToolStripSeparator());

            _devicesSubMenu = new ToolStripMenuItem("📱 Dispositivo");
            menu.Items.Add(_devicesSubMenu);

            _styleSubMenu = new ToolStripMenuItem("🎨 Estilo de Icono");
            _styleNumberItem = new ToolStripMenuItem("Número con barra de carga", null, (s, e) => SetIconStyle(IconStyle.Number))
            {
                Checked = true
            };
            _styleBatteryItem = new ToolStripMenuItem("Icono clásico de batería", null, (s, e) => SetIconStyle(IconStyle.Battery));
            _styleSubMenu.DropDownItems.Add(_styleNumberItem);
            _styleSubMenu.DropDownItems.Add(_styleBatteryItem);
            menu.Items.Add(_styleSubMenu);

            var sizeSubMenu = new ToolStripMenuItem("🔎 Tamaño del icono");
            _sizeSmallItem = new ToolStripMenuItem("Pequeño", null, (s, e) => SetIconSize(IconSize.Small));
            _sizeNormalItem = new ToolStripMenuItem("Normal", null, (s, e) => SetIconSize(IconSize.Normal))
            {
                Checked = true
            };
            _sizeLargeItem = new ToolStripMenuItem("Grande", null, (s, e) => SetIconSize(IconSize.Large));
            sizeSubMenu.DropDownItems.Add(_sizeSmallItem);
            sizeSubMenu.DropDownItems.Add(_sizeNormalItem);
            sizeSubMenu.DropDownItems.Add(_sizeLargeItem);
            menu.Items.Add(sizeSubMenu);

            menu.Items.Add("🔄 Actualizar ahora", null, async (s, e) => await RefreshBatteryStatusAsync());
            menu.Items.Add("⚙️ Ajustes de Bluetooth", null, (s, e) => OpenBluetoothSettings());

            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem("🚀 Iniciar con Windows", null, async (s, e) => await ToggleStartupWithWindowsAsync())
            {
                Checked = !IsPackagedProcess() && IsStartupWithWindowsEnabled()
            };
            menu.Items.Add(_startupItem);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("❌ Salir", null, (s, e) => ExitApplication());

            return menu;
        }

        private async Task RefreshBatteryStatusAsync()
        {
            if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
            {
                Log("Refresh ya en curso, omitiendo.");
                return;
            }

            try
            {
                Log("Iniciando escaneo de dispositivos Bluetooth...");
                var devices = await _batteryService.ScanDevicesAsync();
                Log($"Escaneo completado. Dispositivos encontrados: {devices.Count}");

                RunOnUi(() =>
                {
                    _cachedDevices = devices;

                    BluetoothDeviceInfo? targetDevice = null;

                    if (_selectedDeviceAddress.HasValue)
                    {
                        targetDevice = devices.FirstOrDefault(d => d.Address == _selectedDeviceAddress.Value);
                    }

                    if (targetDevice == null)
                    {
                        targetDevice = devices.FirstOrDefault(d => d.IsConnected && d.BatteryPercentage.HasValue)
                                    ?? devices.FirstOrDefault(d => d.IsConnected)
                                    ?? devices.FirstOrDefault();
                    }

                    UpdateDeviceMenu();

                    if (targetDevice != null)
                    {
                        string status = targetDevice.IsConnected
                            ? (targetDevice.BatteryPercentage.HasValue ? $"{targetDevice.BatteryPercentage.Value}%" : "Conectado")
                            : "Desconectado";

                        Log($"Dispositivo seleccionado: '{targetDevice.Name}', Estado: {status}");

                        _headerMenuItem.Text = $"🎧 {targetDevice.Name}: {status}";

                        string tooltip = $"{targetDevice.Name}: {status}";
                        if (tooltip.Length >= NotifyIconMaxTooltipLength)
                        {
                            tooltip = tooltip.Substring(0, NotifyIconMaxTooltipLength - 3) + "...";
                        }
                        _notifyIcon.Text = tooltip;

                        UpdateIcon(targetDevice.BatteryPercentage, targetDevice.IsConnected);
                    }
                    else
                    {
                        Log("No se encontraron dispositivos.");
                        _headerMenuItem.Text = "No hay dispositivos Bluetooth";
                        _notifyIcon.Text = "Bticon - Sin dispositivos";
                        UpdateIcon(null, false);
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Error en RefreshBatteryStatusAsync: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInProgress, 0);
            }
        }

        private void UpdateDeviceMenu()
        {
            _devicesSubMenu.DropDownItems.Clear();

            var autoItem = new ToolStripMenuItem("Automático (Recomendado)", null, (s, e) =>
            {
                _selectedDeviceAddress = null;
                _ = RefreshBatteryStatusAsync();
            })
            {
                Checked = !_selectedDeviceAddress.HasValue
            };
            _devicesSubMenu.DropDownItems.Add(autoItem);
            _devicesSubMenu.DropDownItems.Add(new ToolStripSeparator());

            if (_cachedDevices.Count == 0)
            {
                var noneItem = new ToolStripMenuItem("No se encontraron dispositivos emparejados") { Enabled = false };
                _devicesSubMenu.DropDownItems.Add(noneItem);
                return;
            }

            foreach (var dev in _cachedDevices)
            {
                string info = dev.IsConnected
                    ? (dev.BatteryPercentage.HasValue ? $" ({dev.BatteryPercentage.Value}%)" : " (Conectado)")
                    : " (Desconectado)";

                var devItem = new ToolStripMenuItem($"{dev.Name}{info}", null, (s, e) =>
                {
                    _selectedDeviceAddress = dev.Address;
                    _ = RefreshBatteryStatusAsync();
                })
                {
                    Checked = _selectedDeviceAddress.HasValue && _selectedDeviceAddress.Value == dev.Address
                };
                _devicesSubMenu.DropDownItems.Add(devItem);
            }
        }

        private void UpdateIcon(byte? batteryPercentage, bool isConnected)
        {
            _currentBatteryPercentage = batteryPercentage;
            _isCurrentDeviceConnected = isConnected;

            try
            {
                var newIcon = IconGenerator.CreateBatteryIcon(
                    batteryPercentage,
                    isConnected,
                    _currentStyle,
                    _currentIconSize);
                var oldIcon = _currentIcon;
                var oldHIcon = _currentHIcon;

                _currentIcon = newIcon;
                _currentHIcon = newIcon.Handle;
                _notifyIcon.Icon = newIcon;

                // Solo después de asignar el nuevo icono destruimos de forma segura el handle anterior
                if (oldHIcon != IntPtr.Zero)
                {
                    try { DestroyIcon(oldHIcon); } catch { }
                }
                oldIcon?.Dispose();

                Log($"Icono actualizado: Nivel={batteryPercentage}%, Conectado={isConnected}");
            }
            catch (Exception ex)
            {
                Log($"Error en UpdateIcon: {ex}");
            }
        }

        private void SetIconStyle(IconStyle style)
        {
            _currentStyle = style;
            _styleNumberItem.Checked = (style == IconStyle.Number);
            _styleBatteryItem.Checked = (style == IconStyle.Battery);
            _ = RefreshBatteryStatusAsync();
        }

        private void SetIconSize(IconSize size)
        {
            _currentIconSize = size;
            _sizeSmallItem.Checked = (size == IconSize.Small);
            _sizeNormalItem.Checked = (size == IconSize.Normal);
            _sizeLargeItem.Checked = (size == IconSize.Large);
            UpdateIcon(_currentBatteryPercentage, _isCurrentDeviceConnected);
        }

        private void OpenBluetoothSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:bluetooth") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la configuración: {ex.Message}", "Bticon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool IsStartupWithWindowsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey, false);
                return key?.GetValue(AppRegistryValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task UpdateStartupMenuStateAsync()
        {
            try
            {
                if (IsPackagedProcess())
                {
                    var startupTask = await StartupTask.GetAsync(StartupTaskId);
                    _startupItem.Checked = startupTask.State == StartupTaskState.Enabled
                        || startupTask.State == StartupTaskState.EnabledByPolicy;
                }
            }
            catch (Exception ex)
            {
                Log($"Error al consultar el inicio con Windows: {ex}");
                MessageBox.Show($"No se pudo consultar el inicio con Windows: {ex.Message}", "Bticon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task ToggleStartupWithWindowsAsync()
        {
            try
            {
                if (IsPackagedProcess())
                {
                    var startupTask = await StartupTask.GetAsync(StartupTaskId);
                    if (startupTask.State == StartupTaskState.Enabled
                        || startupTask.State == StartupTaskState.EnabledByPolicy)
                    {
                        startupTask.Disable();
                    }
                    else
                    {
                        await startupTask.RequestEnableAsync();
                    }

                    _startupItem.Checked = startupTask.State == StartupTaskState.Enabled
                        || startupTask.State == StartupTaskState.EnabledByPolicy;
                    return;
                }

                using var key = Registry.CurrentUser.OpenSubKey(AppRegistryKey, true);
                if (key == null) return;

                bool isCurrentlyEnabled = key.GetValue(AppRegistryValueName) != null;
                if (isCurrentlyEnabled)
                {
                    key.DeleteValue(AppRegistryValueName, false);
                    _startupItem.Checked = false;
                }
                else
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppRegistryValueName, $"\"{exePath}\"");
                    _startupItem.Checked = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al configurar inicio con Windows: {ex.Message}", "Bticon", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsPackagedProcess()
        {
            uint packageFullNameLength = 0;
            int result = GetCurrentPackageFullName(ref packageFullNameLength, IntPtr.Zero);

            if (result == AppModelErrorNoPackage) return false;
            if (result == ErrorInsufficientBuffer || result == 0) return true;

            throw new Win32Exception(result, "No se pudo determinar si Bticon se está ejecutando como una aplicación empaquetada.");
        }

        private void ExitApplication()
        {
            Log("Cerrando aplicación por solicitud del usuario...");
            _notifyIcon.Visible = false;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _batteryService.Dispose();

            if (_currentHIcon != IntPtr.Zero)
            {
                try { DestroyIcon(_currentHIcon); } catch { }
                _currentHIcon = IntPtr.Zero;
            }
            _currentIcon?.Dispose();
            _notifyIcon.Dispose();
            _invoker.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Dispose();
                _batteryService.Dispose();
                if (_currentHIcon != IntPtr.Zero)
                {
                    try { DestroyIcon(_currentHIcon); } catch { }
                    _currentHIcon = IntPtr.Zero;
                }
                _currentIcon?.Dispose();
                _notifyIcon.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
