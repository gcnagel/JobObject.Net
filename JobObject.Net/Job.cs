﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Stormancer.JobManagement
{
    public class Job : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr a, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, UInt32 cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        private IntPtr handle;
        private bool disposed;

        public Job()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Job object already exists.");
            }

            UpdateMemoryLimit(null, null, null);
        }

        ~Job()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
                return;

            Interop.CloseHandle(handle);
            handle = IntPtr.Zero;
            disposed = true;
        }

        public void UpdateMemoryLimit(uint? minPrcWorkingSet, uint? maxPrcWorkingSet, uint? maxJobVirtualMemory)
        {
            if (disposed) throw new ObjectDisposedException("Job is already disposed.");

            var flags = JOBOBJECT_BASIC_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            var basicLimits = new JOBOBJECT_BASIC_LIMIT_INFORMATION();
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = basicLimits,

            };
            if (minPrcWorkingSet.HasValue && maxPrcWorkingSet.HasValue)
            {
                flags |= JOBOBJECT_BASIC_LIMIT_FLAGS.JOB_OBJECT_LIMIT_WORKINGSET;
                basicLimits.MinimumWorkingSetSize = (UIntPtr)minPrcWorkingSet.Value;
                basicLimits.MaximumWorkingSetSize = (UIntPtr)maxPrcWorkingSet.Value;

            }

            if (maxJobVirtualMemory.HasValue)
            {
                flags |= JOBOBJECT_BASIC_LIMIT_FLAGS.JOB_OBJECT_LIMIT_JOB_MEMORY;
                extendedInfo.JobMemoryLimit = (UIntPtr)maxJobVirtualMemory.Value;
            }

            basicLimits.LimitFlags = (uint)flags;
            
            extendedInfo.BasicLimitInformation = basicLimits;

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                if (!SetInformationJobObject(handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to set extended job information.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        /// <summary>
        /// Updates the CPU rate limit associated with the job.
        /// </summary>
        /// <param name="value">The limit in total CPU percent.</param>
        public void UpdateCpuRateLimit(double value)
        {
            if (disposed) throw new ObjectDisposedException("Job is already disposed.");

            var cpuRateInfo = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
            {
                ControlFlags = 1 | 4,
                CpuRate = (uint)(value * 100)
            };
            var length = Marshal.SizeOf(typeof(JOBOBJECT_CPU_RATE_CONTROL_INFORMATION));
            IntPtr cpuRateInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(cpuRateInfo, cpuRateInfoPtr, false);
                if (!SetInformationJobObject(handle, JobObjectInfoType.JobObjectCpuRateControlInformation, cpuRateInfoPtr, (uint)length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to set cpu rate job information.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(cpuRateInfoPtr);
            }
        }

        public void AddProcess(IntPtr processHandle)
        {
            if (!AssignProcessToJobObject(handle, processHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to add process to JobObject.");
            }
        }

        public void AddProcess(int processId)
        {
            AddProcess(Process.GetProcessById(processId));
        }

        public void AddProcess(Process process)
        {
            AddProcess(process.Handle);
        }
    }

    #region Helper classes

    [StructLayout(LayoutKind.Sequential)]
    internal class IO_COUNTERS
    {
        public UInt64 ReadOperationCount;
        public UInt64 WriteOperationCount;
        public UInt64 OtherOperationCount;
        public UInt64 ReadTransferCount;
        public UInt64 WriteTransferCount;
        public UInt64 OtherTransferCount;
    }

    internal enum JOBOBJECT_BASIC_LIMIT_FLAGS
    {
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000,
        JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200,
        JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100,
        JOB_OBJECT_LIMIT_WORKINGSET = 0x00000001,


    }
    [StructLayout(LayoutKind.Sequential)]
    internal class JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public Int64 PerProcessUserTimeLimit;
        public Int64 PerJobUserTimeLimit;
        public UInt32 LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public UInt32 ActiveProcessLimit;
        public UIntPtr Affinity;
        public UInt32 PriorityClass;
        public UInt32 SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class SECURITY_ATTRIBUTES
    {
        public UInt32 nLength;
        public IntPtr lpSecurityDescriptor;
        public Int32 bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        public UInt32 ControlFlags;
        public UInt32 CpuRate;

    }

    internal enum JobObjectInfoType
    {
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11,
        JobObjectCpuRateControlInformation = 15
    }

    #endregion

}