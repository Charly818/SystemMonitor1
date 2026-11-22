using System;
using System.IO;

namespace SystemMonitor
{
    internal static class Log
    {
        static readonly object Lock = new object();

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemMonitor"); }
        }

        public static string FilePath { get { return Path.Combine(Dir, "log.txt"); } }

        public static void Init()
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, ""); } catch { }
        }

        public static void Write(string msg)
        {
            try
            {
                lock (Lock)
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss ") + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
