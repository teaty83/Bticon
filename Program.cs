using System;
using System.Threading;
using System.Windows.Forms;

namespace Bticon
{
    internal static class Program
    {
        private const string AppMutexName = "Bticon_SingleInstance_Mutex_9B8AAC9B26DF";

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogException("AppDomain UnhandledException", e.ExceptionObject as Exception);
            };

            Application.ThreadException += (s, e) =>
            {
                LogException("Application ThreadException", e.Exception);
            };

            using var mutex = new Mutex(true, AppMutexName, out bool createdNew);
            if (!createdNew)
            {
                TrayApplicationContext.Log("Otra instancia de Bticon ya está en ejecución (Mutex ocupado).");
                MessageBox.Show(
                    "Bticon ya se está ejecutando en la barra de tareas (área de notificación).",
                    "Bticon",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                TrayApplicationContext.Log("Inicializando ApplicationConfiguration...");
                ApplicationConfiguration.Initialize();
                TrayApplicationContext.Log("Iniciando Application.Run...");
                Application.Run(new TrayApplicationContext());
                TrayApplicationContext.Log("Application.Run ha finalizado.");
            }
            catch (Exception ex)
            {
                LogException("Application.Run Exception", ex);
                MessageBox.Show(
                    $"Error inesperado en Bticon:\n{ex.Message}\n\nRevisa el archivo de log en la carpeta de la aplicación.",
                    "Bticon - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                TrayApplicationContext.Log("Main() ha terminado.");
            }
        }

        private static void LogException(string context, Exception? ex)
        {
            if (ex == null) return;
            try
            {
                TrayApplicationContext.Log($"[ERROR] [{context}] {ex}");
            }
            catch { }
        }
    }
}
