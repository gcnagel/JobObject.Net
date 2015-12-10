using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Stormancer.JobManagement
{
    /// <summary>
    /// A factory that creates processes with the CREATE_BREAKAWAY_FROM_JOB set, that enables to assign the process to jobs even if the current process is itself running inside a job (That's the case when running the program in Visual Studio for instance) 
    /// </summary>
    /// <remarks>Edited with reference to Microsoft corefx library. MIT license: https://github.com/dotnet/corefx/blob/master/LICENSE</remarks>
    public class ProcessFactory
    {
        private const int CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
        private const int CREATE_NO_WINDOW = 0x08000000;
        private const int CREATE_SUSPENDED = 0x00000004;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, EntryPoint = "CreateProcessW")]
        private static extern bool CreateProcess(
            [MarshalAs(UnmanagedType.LPTStr)] string lpApplicationName,
            [MarshalAs(UnmanagedType.LPTStr)] string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            [MarshalAs(UnmanagedType.LPTStr)] string lpCurrentDirectory,
            STARTUPINFO lpStartupInfo,
            PROCESS_INFORMATION lpProcessInformation
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hObject, int uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int ResumeThread(IntPtr hThread);

        public static System.Diagnostics.Process CreateProcess(string path, string args, string workingDirectory)
        {
            return CreateProcess(path, args, workingDirectory, null);
        }

        public static System.Diagnostics.Process CreateProcess(string path, string args, string workingDirectory, Job job)
        {
            PROCESS_INFORMATION processInfo = new PROCESS_INFORMATION();
            try
            {
                STARTUPINFO startupInfo = new STARTUPINFO();
                startupInfo.cb = (uint)Marshal.SizeOf(typeof(STARTUPINFO));

                string command = path ?? "";
                if (!command.StartsWith("\"", StringComparison.Ordinal) || !command.EndsWith("\"", StringComparison.Ordinal))
                {
                    command = "\"" + command + "\"";
                }
                if (!string.IsNullOrEmpty(args))
                {
                    command += " " + args;
                }

                int creationFlags = CREATE_BREAKAWAY_FROM_JOB | CREATE_NO_WINDOW;
                if (job != null)
                {
                    creationFlags |= CREATE_SUSPENDED;
                }

                if (!CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, false, creationFlags, IntPtr.Zero, workingDirectory, startupInfo, processInfo))
                {
                    throw new Win32Exception();
                }

                if (job != null)
                {
                    job.AddProcess(processInfo.hProcess);

                    if (ResumeThread(processInfo.hThread) == -1)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start process.");
                    }
                }

                return Process.GetProcessById(processInfo.ProcessId);
            }
            catch (Win32Exception e)
            {
                if (processInfo.hProcess != IntPtr.Zero && processInfo.hProcess != INVALID_HANDLE_VALUE)
                {
                    if (!TerminateProcess(processInfo.hProcess, -1))
                    {
                        throw new Win32Exception("Failed to terminate process with error code " + Marshal.GetLastWin32Error() + ".", e);
                    }
                }

                throw;
            }
            finally
            {
                if (processInfo.hProcess != IntPtr.Zero && processInfo.hProcess != INVALID_HANDLE_VALUE)
                {
                    Interop.CloseHandle(processInfo.hProcess);
                }
                if (processInfo.hThread != IntPtr.Zero && processInfo.hThread != INVALID_HANDLE_VALUE)
                {
                    Interop.CloseHandle(processInfo.hThread);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal class PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public Int32 ProcessId;
        public Int32 ThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal class STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }
}

