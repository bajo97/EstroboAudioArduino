using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EstroboAudioArduinoWF
{
    public class ProcessScanner
    {
        public List<string> DetectedProcesses { get; private set; } = new List<string>();

        public void ScanAndSave(string outputPath = "procesos_activos.txt")
        {
            DetectedProcesses.Clear();

            using (var writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("Procesos activos detectados:");
                writer.WriteLine("----------------------------");

                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        string name = process.ProcessName;
                        DetectedProcesses.Add(name);
                        writer.WriteLine(name);
                    }
                    catch
                    {
                        // algunos procesos pueden lanzar excepciones al intentar acceder a sus propiedades
                    }
                }
            }
        }

        public List<string> GetDetectedProcesses()
        {
            return new List<string>(DetectedProcesses);
        }
    }
}
