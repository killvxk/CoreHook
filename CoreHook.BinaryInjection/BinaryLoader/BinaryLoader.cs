﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using CoreHook.Unmanaged;
using System.IO;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace CoreHook.BinaryInjection
{
    public class BinaryLoader: IBinaryLoader
    {
        public void Load(Process targetProcess, string binaryPath, IEnumerable<string> dependencies = null, string dir = null)
        {
            if (dependencies != null)
            {
                foreach (var binary in dependencies)
                {
                    var fname = Path.Combine(dir, binary);
                    if (!File.Exists(fname))
                    {
                        throw new FileNotFoundException("Binary file not found.", binary);
                    }
                  
                    var moduleName = Path.GetFileName(binary);

                    if (targetProcess.GetModuleHandleByBaseName(moduleName) == IntPtr.Zero)
                    {
                        targetProcess.LoadLibrary(fname);

                        var x = 0;
                        for (; x < 50 && targetProcess.GetModuleHandleByBaseName(moduleName) == IntPtr.Zero; x++)
                        {
                            Thread.Sleep(200);
                        }

                        if (x == 50)
                        {
                            throw new TimeoutException($"'{binary}' failed to load into the process.");
                        }
                    }
                    
                }
            }

            targetProcess.LoadLibrary(binaryPath);
        }
        public void Execute(Process process, string module, string function, string args)
        {
            var argBytes = Encoding.Unicode.GetBytes(args + "\0");

            process.Execute(module, function, argBytes);
        }
        public void ExecuteWithArgs(Process process, string module, BinaryLoaderArgs args)
        {
            Control.ExecuteAssemblyWithArgs(process, module, args);
        }
        public void CallFunctionWithArgs(Process process, string module, string function, byte[] arguments)
        {
            Control.LoadAssemblyWithArgs(process, module, new FunctionCallArgs(function, arguments));
        }
        public void CallFunctionWithRemoteArgs(Process process, string module, string function, IntPtr arguments)
        {
            Control.LoadAssemblyWithArgs(process, module, new FunctionCallArgs(function, arguments));
        }
    }
}