using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace OpenCodeProvidersTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Must be attached before any method that touches Newtonsoft is jitted,
            // which is why Main itself refers to it only through MainForm.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportFatal(e.ExceptionObject as Exception);

            // Optional startup arguments: a config path to open, and/or --page <name>.
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--page" && i + 1 < args.Length)
                {
                    MainForm.StartupPage = args[++i];
                }
                else if (!arg.StartsWith("-"))
                {
                    MainForm.StartupConfig = arg;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }

        /// <summary>Loads the Newtonsoft.Json copy embedded in this exe.</summary>
        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            string requested = new AssemblyName(args.Name).Name;
            if (requested != "Newtonsoft.Json") return null;

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Newtonsoft.Json.dll"))
            {
                if (stream == null) return null;
                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int chunk = stream.Read(buffer, read, buffer.Length - read);
                    if (chunk <= 0) break;
                    read += chunk;
                }
                return Assembly.Load(buffer);
            }
        }

        /// <summary>
        /// A crash in a config editor must not silently take the window down, so the
        /// details are written next to the temp folder and shown instead of a raw dump.
        /// </summary>
        private static void ReportFatal(Exception error)
        {
            if (error == null) return;

            string logPath = "";
            try
            {
                logPath = Path.Combine(Path.GetTempPath(), "OpenCodeProvidersTool-error.log");
                File.WriteAllText(logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + error);
            }
            catch { }

            try
            {
                MessageDialog.Show(null, "Unexpected error",
                    error.Message + "\r\n\r\nDetails written to:\r\n" + logPath, MessageKind.Error);
            }
            catch
            {
                MessageBox.Show(error.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
