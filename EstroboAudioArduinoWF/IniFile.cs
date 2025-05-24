using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace EstroboAudioArduinoWF
{
    public class IniFile
    {
        private readonly string path;

        public IniFile(string fileName = "config.ini")
        {
            string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            path = Path.Combine(exePath, fileName);

            // Crear archivo por defecto si no existe
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "[Serial]\nPort=COM7");
            }
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string defaultVal, StringBuilder retVal, int size, string filePath);

        public string Read(string section, string key, string defaultVal = "")
        {
            var sb = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultVal, sb, sb.Capacity, path);
            return sb.ToString();
        }

        public void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, path);
        }
    }
}
