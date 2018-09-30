﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;

namespace CoreHook
{
    /// <summary>
    /// This class is intended to be used within hook handlers,
    /// to access associated runtime information.
    /// </summary>
    /// <remarks>
    /// Other hooking libraries on the market require that you keep track of
    /// such information yourself, what can be a burden.
    /// </remarks>
    public class HookRuntimeInfo
    {
        private static ProcessModule[] ModuleArray = new ProcessModule[0];
        private static long LastUpdate = 0;

        /// <summary>
        ///	Is the current thread within a valid hook handler? This is only the case
        ///	if your handler was called through the hooked entry point...
        ///	Executes in max. one micro secound.
        /// </summary>
        public static bool IsHandlerContext
        {
            get
            {
                IntPtr Callback;

                if (NativeAPI.Is64Bit)
                {
                    return NativeAPI_x64.DetourBarrierGetCallback(out Callback) == NativeAPI.STATUS_SUCCESS;
                }
                else
                {
                    return NativeAPI_x86.DetourBarrierGetCallback(out Callback) == NativeAPI.STATUS_SUCCESS;
                }

            }
        }

        ///	<summary>
        ///	The user callback initially passed to either <see cref="LocalHook.Create"/> or <see cref="LocalHook.CreateUnmanaged"/>.
        /// Executes in max. one micro secound.
        ///	</summary>
        ///	<exception cref="NotSupportedException"> The current thread is not within a valid hook handler. </exception>
        public static object Callback
        {
            get
            {
                return Handle.Callback;
            }
        }

        ///	<summary>
        ///	The hook handle initially returned by either <see cref="LocalHook.Create"/> or <see cref="LocalHook.CreateUnmanaged"/>.
        /// Executes in max. one micro secound.
        ///	</summary>
        ///	<exception cref="NotSupportedException"> The current thread is not within a valid hook handler. </exception>
        public static LocalHook Handle
        {
            get
            {
                IntPtr Callback;

                NativeAPI.DetourBarrierGetCallback(out Callback);

                if (Callback == IntPtr.Zero)
                {
                    return null;
                }

                return (LocalHook)GCHandle.FromIntPtr(Callback).Target;
            }
        }

        /// <summary>
        /// Allows you to explicitly update the unmanaged module list which is required for
        /// <see cref="CallingUnmanagedModule"/>, <see cref="UnmanagedStackTrace"/> and <see cref="PointerToModule"/>. 
        /// Normally this is not necessary, but if you hook a process that frequently loads/unloads modules, you
        /// may call this method in a <c>LoadLibrary</c> hook to always operate on the latest module list.
        /// </summary>
        public static void UpdateUnmanagedModuleList()
        {
            List<ProcessModule> ModList = new List<ProcessModule>();

            foreach (ProcessModule Module in Process.GetCurrentProcess().Modules)
            {
                ModList.Add(Module);
            }

            ModuleArray = ModList.ToArray();

            LastUpdate = DateTime.Now.Ticks;
        }

        /// <summary>
        /// Retrives the unmanaged module that contains the given pointer. If no module can be
        /// found, <c>null</c> is returned. This method will automatically update the unmanaged
        /// module list from time to time.
        /// Executes in less than one micro secound.
        /// </summary>
        /// <param name="InPointer"></param>
        /// <returns></returns>
        public static ProcessModule PointerToModule(IntPtr InPointer)
        {
            long Pointer = InPointer.ToInt64();

            if ((Pointer == 0) || (Pointer == ~0))
                return null;

            TRY_AGAIN:
            for (int i = 0; i < ModuleArray.Length; i++)
            {
                if ((Pointer >= ModuleArray[i].BaseAddress.ToInt64()) &&
                    (Pointer <= ModuleArray[i].BaseAddress.ToInt64() + ModuleArray[i].ModuleMemorySize))
                {
                    return ModuleArray[i];
                }
            }

            if ((DateTime.Now.Ticks - LastUpdate) > 1000 * 1000 * 10 /* 1000 ms*/)
            {
                UpdateUnmanagedModuleList();

                goto TRY_AGAIN;
            }

            return null;
        }

        /// <summary>
        /// Determines the first unmanaged module on the current call stack. This is always the module
        /// that invoked the hook. 
        /// Executes in max. 15 micro secounds.
        /// </summary>
        /// <remarks>
        /// The problem is that if the calling module is a NET assembly
        /// and invokes the hook through a P-Invoke binding, you will get
        /// "mscorwks.dll" as calling module and not the NET assembly. This is only an example 
        /// but I think you got the idea. To solve this issue, refer to <see cref="UnmanagedStackTrace"/>
        /// and <see cref="ManagedStackTrace"/>!
        /// </remarks>
        public static ProcessModule CallingUnmanagedModule
        {
            get
            {
                return PointerToModule(ReturnAddress);
            }
        }

        /// <summary>
        /// Determines the first managed module on the current call stack. This is always the module
        /// that invoked the hook. 
        /// Executes in max. 40 micro secounds.
        /// </summary>
        /// <remarks>
        /// Imagine your hook targets CreateFile. A NET assembly will now invoke this hook through
        /// FileStream, for example. But because System.IO.FileStream invokes the hook, you will
        /// get "System.Core" as calling module and not the desired assembly.
        /// To solve this issue, refer to <see cref="UnmanagedStackTrace"/>
        /// and <see cref="ManagedStackTrace"/>!
        /// </remarks>
        public static System.Reflection.Assembly CallingManagedModule
        {
            get
            {
                IntPtr Backup;

                NativeAPI.DetourBarrierBeginStackTrace(out Backup);

                try
                {
                    return System.Reflection.Assembly.GetCallingAssembly();
                }
                finally
                {
                    NativeAPI.DetourBarrierEndStackTrace(Backup);
                }
            }
        }

        /// <summary>
        /// Returns the address where execution is continued, after you hook has
        /// been completed. This is always the instruction behind the hook invokation.
        /// Executes in max. one micro secound.
        /// </summary>
        public static IntPtr ReturnAddress
        {
            get
            {
                IntPtr RetAddr;

                NativeAPI.DetourBarrierGetReturnAddress(out RetAddr);

                return RetAddr;
            }
        }

        /// <summary>
        /// A stack address pointing to <see cref="ReturnAddress"/>.
        /// Executes in max. one micro secound.
        /// </summary>
        public static IntPtr AddressOfReturnAddress
        {
            get
            {
                IntPtr AddrOfRetAddr;

                NativeAPI.DetourBarrierGetAddressOfReturnAddress(out AddrOfRetAddr);

                return AddrOfRetAddr;
            }
        }

        private class StackTraceBuffer : CriticalFinalizerObject
        {
            public IntPtr Unmanaged;
            public IntPtr[] Managed;
            public ProcessModule[] Modules;

            public StackTraceBuffer()
            {
                if ((Unmanaged = Marshal.AllocCoTaskMem(64 * IntPtr.Size)) == IntPtr.Zero)
                    throw new OutOfMemoryException();

                Managed = new IntPtr[64];
                Modules = new ProcessModule[64];
            }

            public void Synchronize(int InCount)
            {
                Marshal.Copy(Unmanaged, Managed, 0, Math.Min(64, InCount));
            }

            ~StackTraceBuffer()
            {
                if (Unmanaged != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(Unmanaged);
                }

                Unmanaged = IntPtr.Zero;
            }
        }

        [ThreadStatic]
        private static StackTraceBuffer StackBuffer = null;

        /// <summary>
        /// Creates a call stack trace of the unmanaged code path that finally
        /// lead to your hook. To detect whether the desired module is within the
        /// call stack you will have to walk through the whole list!
        /// Executes in max. 20 micro secounds.
        /// </summary>
        /// <remarks>
        /// This method is not supported on Windows 2000 and will just return the
        /// calling unmanaged module wrapped in an array on that platform.
        /// </remarks>
        public static ProcessModule[] UnmanagedStackTrace
        {
            get
            {
                // not supported on windows 2000
                if ((Environment.OSVersion.Version.Major == 5) && (Environment.OSVersion.Version.Minor == 0))
                {
                    ProcessModule[] Module = new ProcessModule[1];

                    Module[0] = CallingUnmanagedModule;

                    return Module;
                }

                IntPtr Backup;

                NativeAPI.DetourBarrierBeginStackTrace(out Backup);

                try
                {
                    if (StackBuffer == null)
                    {
                        StackBuffer = new StackTraceBuffer();
                    }

                    short Count = NativeAPI.RtlCaptureStackBackTrace(0, 32, StackBuffer.Unmanaged, IntPtr.Zero);
                    ProcessModule[] Result = new ProcessModule[Count];

                    StackBuffer.Synchronize(Count);

                    for (int i = 0; i < Count; i++)
                    {
                        Result[i] = PointerToModule(StackBuffer.Managed[i]);
                    }

                    return Result;
                }
                finally
                {
                    NativeAPI.DetourBarrierEndStackTrace(Backup);
                }
            }
        }

        /// <summary>
        /// Creates a call stack trace of the managed code path that finally
        /// lead to your hook. To detect whether the desired module is within the
        /// call stack you will have to walk through the whole list!
        /// Executes in max. 80 micro secounds.
        /// </summary>
        public static System.Reflection.Module[] ManagedStackTrace
        {
            get
            {
                IntPtr Backup;

                NativeAPI.DetourBarrierBeginStackTrace(out Backup);

                try
                {
                    StackFrame[] Frames = new StackTrace().GetFrames();
                    System.Reflection.Module[] Result = new System.Reflection.Module[Frames.Length];

                    for (int i = 0; i < Frames.Length; i++)
                    {
                        Result[i] = Frames[i].GetMethod().Module;
                    }

                    return Result;
                }
                finally
                {
                    NativeAPI.DetourBarrierEndStackTrace(Backup);
                }
            }
        }
    }

}
