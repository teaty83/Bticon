# Bticon - Monitor de Batería Bluetooth en la Barra de Tareas

**Bticon** es una aplicación ultraligera y nativa para Windows 10 y Windows 11 desarrollada en **C# (.NET 8)** que muestra en tiempo real el porcentaje y estado de la batería de tus auriculares y dispositivos Bluetooth directamente como un icono en el área de notificación (System Tray) de la barra de tareas.

---

## 🚀 Características Principales

1. **Lectura Nativa en Tiempo Real**:
   - Soporte para perfiles **HFP (Bluetooth Classic)** mediante la API de Windows `CfgMgr32` y claves de dispositivo nativas (`DEVPKEY_Bluetooth_HfpBattery`).
   - Soporte para **BLE (Bluetooth Low Energy)** mediante el servicio estándar GATT de batería (`0x180F` / `0x2A19`).
   - Sin drivers externos ni software pesado.

2. **Icono Dinámico y Adaptable**:
   - **Modo Numérico (Por defecto)**: Muestra el número exacto del porcentaje de batería (ej. `70`) con una barra inferior coloreada según el nivel:
     - 🟢 **Verde** (> 50%)
     - 🟡 **Ámbar / Naranja** (21% - 50%)
     - 🔴 **Rojo** (≤ 20%)
   - **Modo Batería Gráfica**: Icono tradicional de batería con nivel de relleno visual y porcentaje.
   - Estado **Desconectado**: Muestra una silueta en gris con indicador cuando los auriculares están apagados o fuera de alcance.

3. **Menú Contextual (Clic derecho en el icono)**:
   - **Cabecera**: Dispositivo activo y porcentaje actual (`🎧 REDMI Buds 8 Pro: 70%`).
   - **📱 Dispositivo**: Permite alternar entre selección automática (recomendado) o elegir manualmente qué dispositivo emparejado monitorear.
   - **🎨 Estilo de Icono**: Alterna entre número o gráfica de batería al instante.
   - **🔎 Tamaño del icono**: Permite elegir entre **Pequeño** (tamaño anterior), **Normal** (predeterminado actual) y **Grande**.
   - **🔄 Actualizar ahora**: Fuerza una lectura inmediata.
   - **⚙️ Ajustes de Bluetooth**: Abre directamente la ventana de configuración de Bluetooth de Windows (también disponible con doble clic en el icono).
   - **🚀 Iniciar con Windows**: Habilita o deshabilita el arranque automático del programa al encender la PC (con un solo clic).
   - **❌ Salir**: Cierra la aplicación liberando todos los recursos.

4. **Bajo Consumo de Recursos**:
   - Consumo de CPU: **0.0%** en reposo.
   - Escaneo periódico de 30 milisegundos cada 20 segundos y reactivo a eventos de conexión.
   - Prevención de instancias duplicadas integrada (`Mutex`).

---

## 📦 Ejecución

Para ejecutar la aplicación, compílala desde el código fuente siguiendo las instrucciones de la sección de compilación. También puedes activar **"Iniciar con Windows"** desde el menú contextual con clic derecho.

---

## 🛠️ Compilación y Desarrollo

Para compilar o modificar el código fuente:
```powershell
# Compilación estándar
dotnet build

# Publicación como ejecutable único
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
