using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MiscUtils
{
    public class Utils
    {
        public static void debugWrite(string str)
        {
            Trace.WriteLine(str);
        }

        public static int compVers(string a, string b)
        {
            try
            {
                return new Version(a).CompareTo(new Version(b));
            }
            catch (Exception ex)
            {
                debugWrite(ex.ToString());
                return -100;
            }
        }

        public static string doHTTPReq(string url, string userAgent)
        {
            string ret = null;
            try
            {
                var request = WebRequest.Create(url);
                ((HttpWebRequest)request).UserAgent = userAgent;
                var response = request.GetResponse();
                debugWrite($"StatusCode: {((HttpWebResponse)response).StatusCode}");
                var dataStream = response.GetResponseStream();
                var reader = new StreamReader(dataStream);

                ret = reader.ReadToEnd();
                reader.Close(); //closes the stream and the response
            }
            catch (Exception ex)
            {
                debugWrite(ex.ToString());
            }
            return ret;
        }

        public static int checkVerAgainstURL(string url, string userAgent, string appVer)
        {
            //appVer -> Application.ProductVersion
            //URL should have a version number followed by a newline. later lines are ignored.

            //1 -> newer ver available.
            //0 -> current.
            //-1 -> this version is newer.
            //-100 -> error
            var vInfo = doHTTPReq(url, userAgent);
            if (string.IsNullOrEmpty(vInfo)) { return -100; }
            debugWrite(vInfo);
            try
            {
                var r = new StringReader(vInfo);
                var ver = r.ReadLine();
                if (ver != null) { return compVers(ver, appVer); }
            }
            catch (Exception ex)
            {
                debugWrite(ex.ToString());
            }
            return -100;
        }

        public static string getFnameInAppdata(string fname, string appName, bool localAppData = false)
        {//will create dir if needed, otherwise return a full filename.
            var appData = Environment.GetFolderPath(localAppData ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appData, appName);
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            return Path.Combine(appFolder, fname);
        }

        public static DateTime getFileDate(string fname)
        {//will create a file if needed
            var ret = new DateTime(0);
            try
            {
                if (File.Exists(fname))
                {
                    ret = File.GetLastWriteTime(fname);
                }
                else
                {
                    File.Create(fname).Dispose();
                }
            }
            catch (Exception ex) { debugWrite(ex.ToString()); }
            return ret;
        }
        public static bool setFileDate(string fname)
        {
            try
            {
                File.SetLastWriteTime(fname, DateTime.Now);
                return true;
            }
            catch (Exception ex) { debugWrite(ex.ToString()); }
            return false;
        }
        public static void removeFile(string fname)
        {
            try
            {
                if (File.Exists(fname)) { File.Delete(fname); }
            }
            catch (Exception ex) { debugWrite(ex.ToString()); }
        }
    }

    public class LaunchUtils
    {
        public static string promptForFile(string startFolder, string filter)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.InitialDirectory = startFolder;
            d.Filter = filter;
            if (d.ShowDialog() != true)
            {
                return null;
            }
            return d.FileName;
        }

        public static bool launchGame()
        {//TODO: get game dir from steam files. see https://github.com/soulsmods/ModEngine2/blob/main/launcher/steam_app_path.cpp
            try
            {
                string exename = @"eldenring.exe";
                string path = exename;
                string dir = "";

                string dirGuess1 = @"C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game";
                string dirGuess2 = @"D:\Steam\steamapps\common\ELDEN RING\Game";
                string dirGuess3 = @"D:\SteamLibrary\steamapps\common\ELDEN RING\Game";

                string pathGuess1 = System.IO.Path.Combine(dirGuess1, exename);
                string pathGuess2 = System.IO.Path.Combine(dirGuess2, exename);
                string pathGuess3 = System.IO.Path.Combine(dirGuess3, exename);

                if (File.Exists(path))
                {
                    Utils.debugWrite("Game is in working dir");
                }
                else if (File.Exists(pathGuess1))
                {
                    Utils.debugWrite("Game is at default steam location");
                    path = pathGuess1;
                    dir = dirGuess1;
                }
                else if (File.Exists(pathGuess2))
                {
                    Utils.debugWrite(@"Game is in D:\Steam");
                    path = pathGuess2;
                    dir = dirGuess2;
                }
                else if (File.Exists(pathGuess3))
                {
                    Utils.debugWrite(@"Game is on D:\SteamLibrary");
                    path = pathGuess3;
                    dir = dirGuess3;
                }

                if (!File.Exists(path))
                {
                    MessageBox.Show(@"Please find eldenring.exe. This is normally found in C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game but could be somewhere else if you moved the steam library. (If you run the tool from that folder, you won't need to browse.)", "", MessageBoxButton.OK, MessageBoxImage.Information);
                    string filter = exename + "|" + exename;
                    path = promptForFile(null, filter);
                    if (string.IsNullOrEmpty(path)) { return false; }
                    dir = System.IO.Path.GetDirectoryName(path);
                    exename = System.IO.Path.GetFileName(path);
                    Utils.debugWrite("Will use selected path " + path);
                }
                var psi = new ProcessStartInfo(path);
                psi.EnvironmentVariables["SteamAppId"] = "1245620";
                psi.UseShellExecute = false;
                psi.WorkingDirectory = dir;
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

#if NO_WPF
    //alternative/dummy implementations for console tools
    enum MessageBoxButton { OK }
    enum MessageBoxImage { Information }
    class MessageBox
    {
        public static void Show(string str = "", string str2 = "", MessageBoxButton b = MessageBoxButton.OK, MessageBoxImage i = MessageBoxImage.Information)
        {
            Console.WriteLine($"{str2} {str}");
        }
    }
    class OpenFileDialog
    {
        public string InitialDirectory { get; set; }
        public string Filter { get; set; }
        public string FileName { get; set; }
        public bool? ShowDialog() { return null; }
    }
#endif

    public class AOBScanner : IDisposable
    {
        [DllImport("ntdll.dll")]
        static extern int NtReadVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToRead, ref UInt32 NumberOfBytesRead);

        public uint textOneSize = 0;
        public int textOneAddr = 0;
        public uint textTwoSize = 0;
        public int textTwoAddr = 0;
        public byte[] sectionOne = new byte[0];
        public byte[] sectionTwo = new byte[0];

        public AOBScanner(IntPtr handle, IntPtr baseAddr, int size)
        {
#if !DEBUG
            outputConsole = false;
#endif

            //TODO: switch to .NET 5+ and use PEHeaders?
            //for now: assume two text sections, and aob scan for them, of course.

            var buf = new byte[0x600];
            uint bytesRead = 0;
            NtReadVirtualMemory(handle, baseAddr, buf, (uint)buf.Length, ref bytesRead);
            var dotText = Encoding.ASCII.GetBytes(".text");
            var dummy = new byte[5];
            int textOne = FindBytes(buf, dotText, dummy);
            if (textOne < 0)
            {
                Console.WriteLine("Cannot find text section");
                return;
            }
            textOneSize = BitConverter.ToUInt32(buf, textOne + 8);
            textOneAddr = BitConverter.ToInt32(buf, textOne + 12);
            sectionOne = new byte[textOneSize];
            NtReadVirtualMemory(handle, baseAddr + textOneAddr, sectionOne, textOneSize, ref bytesRead);

            int textTwo = FindBytes(buf, dotText, dummy, textOne + 0x28);
            if (textTwo > 0)
            {
                textTwoSize = BitConverter.ToUInt32(buf, textTwo + 8);
                textTwoAddr = BitConverter.ToInt32(buf, textTwo + 12);
                sectionTwo = new byte[textTwoSize];
                NtReadVirtualMemory(handle, baseAddr + textTwoAddr, sectionTwo, textTwoSize, ref bytesRead);
            }

            Console.ReadLine();
        }

        //originally from https://github.com/Wulf2k/ER-Patcher.git
        //try and keep in sync with https://github.com/kh0nsu/FromAobScan
        public byte[] hs2b(string hex)
        {
            hex = hex.Replace(" ", "");
            hex = hex.Replace("-", "");
            hex = hex.Replace(":", "");

            byte[] b = new byte[hex.Length >> 1];
            for (int i = 0; i <= b.Length - 1; ++i)
            {
                b[i] = (byte)((hex[i * 2] - (hex[i * 2] < 58 ? 48 : (hex[i * 2] < 97 ? 55 : 87))) * 16 + (hex[i * 2 + 1] - (hex[i * 2 + 1] < 58 ? 48 : (hex[i * 2 + 1] < 97 ? 55 : 87))));
            }
            return b;
        }

        public byte[] hs2w(string hex)
        {
            hex = hex.Replace(" ", "");
            hex = hex.Replace("-", "");
            hex = hex.Replace(":", "");

            byte[] wild = new byte[hex.Length >> 1];
            for (int i = 0; i <= wild.Length - 1; ++i)
            {
                if (hex[i * 2].Equals('?'))
                {
                    wild[i] = 1;
                }
            }
            return wild;
        }

        public int FindBytes(byte[] buf, byte[] find, byte[] wild, int startIndex = 0, int lastIndex = -1)
        {
            if (buf == null || find == null || buf.Length == 0 || find.Length == 0 || find.Length > buf.Length) return -1;
            if (lastIndex < 1) { lastIndex = buf.Length - find.Length; }
            for (int i = startIndex; i < lastIndex + 1; i++)
            {
                if (buf[i] == find[0])
                {
                    for (int m = 1; m < find.Length; m++)
                    {
                        if ((buf[i + m] != find[m]) && (wild[m] != 1)) break;
                        if (m == find.Length - 1) return i;
                    }
                }
            }
            return -1;
        }

        public bool outputConsole = true;

        public int findAddr(byte[] buf, int blockVirtualAddr, string find, string desc, int readoffset32 = -1000, int nextInstOffset = -1000, int justOffset = -1000, int startIndex = 0, bool singleMatch = true, Action<int> callback = null)
        {//TODO: for single match and non-zero start index, try zero start index if no match is found?
            int count = 0;

            byte[] fb = hs2b(find);
            byte[] fwb = hs2w(find);

            int index = startIndex;

            int result = -1;

            do
            {
                index = FindBytes(buf, fb, fwb, index);
                if (index != -1)
                {
                    count++;
                    int rva = index + blockVirtualAddr;
                    result = rva;
                    string output = desc + " found at index " + index + " offset hex " + rva.ToString("X2");

                    if (readoffset32 > -1000)
                    {
                        int index32 = index + readoffset32;
                        var val = BitConverter.ToInt32(buf, index32);
                        result = val;
                        output += " raw val " + val.ToString("X2");
                        if (nextInstOffset > -1000)
                        {
                            int next = blockVirtualAddr + index + nextInstOffset + val;
                            result = next;
                            output += " final offset " + next.ToString("X2");
                        }
                    }

                    if (justOffset > -1000)
                    {
                        result = rva + justOffset;
                        output += " with offset " + (rva + justOffset).ToString("X2");
                    }

                    if (outputConsole) { Console.WriteLine(output); }
                    index += fb.Length; //keep searching in case there's multiple.
                }
                if (index != -1 && callback != null) { callback(result); }
            }
            while (index != -1 && !singleMatch);
            if (0 == count) { Console.WriteLine("Nothing found for " + desc); }
            return result;
        }

        public void Dispose()
        {
            sectionOne = null;
            sectionTwo = null;
        }
    }

    public class FileUtils
    {
        public static List<string[]> importGenericTextResource(string name, char separator = '\t')
        {
            var ret = new List<string[]>();
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith(name));
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line = "";
                    while ((line = reader.ReadLine()) != null)
                    {
                        var spl = line.Split(separator);
                        if (spl.Length < 1) { continue; }
                        ret.Add(spl);
                    }
                }
                return ret;
            }
            catch
            {
                return ret;
            }
        }
    }
}
