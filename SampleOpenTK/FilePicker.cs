using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SampleOpenTK
{
    public class FilePicker
    {
        static bool LinuxHasCommand(string command)
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                Arguments = $" -c \"command -v {command} >/dev/null 2>&1\""
            };
            var p = Process.Start(startInfo);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        
        static string RunProcess(string command, string s)
        {
            var pinf = new ProcessStartInfo(command, s);
            pinf.RedirectStandardOutput = true;
            pinf.UseShellExecute = false;
            var p = Process.Start(pinf);
            string output = "";
            p.OutputDataReceived += (sender, e) => {
                output += e.Data + "\n";
            };
            p.BeginOutputReadLine();
            p.WaitForExit();
            if (p.ExitCode == 0)
                return output.Trim();
            else
                return null;
        }
        
        [DllImport("Comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OpenFileName ofn);
        
        [DllImport("Comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetSaveFileName(ref OpenFileName ofn);
        
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
        struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        public static string OpenFile()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (LinuxHasCommand("kdialog"))
                    return RunProcess("kdialog", "--getopenfilename");
                else if (LinuxHasCommand("zenity"))
                    return RunProcess("zenity", "--file-selection");
                else
                    throw new Exception("Neither kdialog nor zenity present");
            }
            else
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.lpstrFilter = "All files(*.*)\0";
                ofn.lpstrFile = new string(new char[256]);
                ofn.nMaxFile = ofn.lpstrFile.Length;
                ofn.lpstrFileTitle = new string(new char[64]);
                ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
                ofn.lpstrTitle = "Open...\0";
                if (GetOpenFileName(ref ofn))
                    return ofn.lpstrFile.TrimEnd('\0');
                else
                    return null;
            }
        }

        public static string SaveFile()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (LinuxHasCommand("kdialog"))
                    return RunProcess("kdialog", "--getsavefilename");
                else if (LinuxHasCommand("zenity"))
                    return RunProcess("zenity", "--file-selection --save");
                else
                    throw new Exception("Neither kdialog nor zenity present");
            }
            else
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.lpstrFilter = "All files(*.*)\0";
                ofn.lpstrFile = new string(new char[256]);
                ofn.nMaxFile = ofn.lpstrFile.Length;
                ofn.lpstrFileTitle = new string(new char[64]);
                ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
                ofn.lpstrTitle = "Save...\0";
                if (GetSaveFileName(ref ofn))
                    return ofn.lpstrFile.TrimEnd('\0');
                else
                    return null;
            }
        }

    }
}