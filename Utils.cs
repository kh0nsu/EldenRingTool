using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
}
