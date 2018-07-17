using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;  

namespace gsharpc
{
  class Program
  {                   
    static void Main(string[] args)
    {
      string helpMessage = "usage: gsharpc <dll files path> or gsharpc -c --gpc <dll files path>\r\n" +
        " '-c' means only compiles to xxxx.uvms files\r\n '--gpc' means only compiles to xxxx.out bytecode files";

      bool onlyCompileToUvms = false; // 如果有-c，只compile到.uvms文件，否则直接生成字节码
      bool generateGpcFiles = false; // 是否产生.gpc文件，如果这个选项被使用，-c相当于同时被使用了
      var toCompileFilePaths = new List<string>();
      var isWindows = Environment.OSVersion.Platform.ToString() == "Win32NT";
      for (int i=0;i<args.Length;++i)
      {
        var arg = args[i];
        if(arg == "-c")
        {
          onlyCompileToUvms = true;
        }
        else if(arg == "-h" || arg == "--help")
        {
          Console.WriteLine(helpMessage);
          return;
        }
        else if(arg == "--gpc")
        {
          generateGpcFiles = true;
          onlyCompileToUvms = false;
        }
        else if(arg.StartsWith("-"))
        {
          Console.WriteLine("not supported option " + arg);
          return;
        }
        else
        {
          toCompileFilePaths.Add(arg);
        }
      }
      if(toCompileFilePaths.Count<1)
      {
        Console.WriteLine(helpMessage);
        return;
      }
      for (int i = 0; i < toCompileFilePaths.Count; ++i)
      {
        var dllFilepath = toCompileFilePaths[i];
        var uvmsOutputFilePath = ILToUvmTranslator.TranslateDotNetDllToUvm(dllFilepath);  // for test
        //var uvmsOutputFilePath = "F:\\gitwork\\gsharpc\\DemoContract1\\bin\\Debug\\DemoContract1.uvms";   // for test
        if (!onlyCompileToUvms)
        {
          // 调用 uvm_ass来生成.out字节码文件，并删除.uvms文件
          var uvmsAssProcess = new Process();
          Console.WriteLine("cur dir is " + Environment.CurrentDirectory);
          var uvmAssFilePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "uvm_ass"));

          uvmAssFilePath = "F:\\gitwork\\gsharpc\\DemoContract1\\bin\\Debug\\uvm_ass"; //test
          uvmsAssProcess.StartInfo.FileName = uvmAssFilePath;
          if(isWindows)
          {
            uvmsAssProcess.StartInfo.FileName += ".exe";
          }
          uvmsAssProcess.StartInfo.UseShellExecute = false;
          uvmsAssProcess.StartInfo.CreateNoWindow = true;
          uvmsAssProcess.StartInfo.Arguments = " " + uvmsOutputFilePath;       
          uvmsAssProcess.Start();
          uvmsAssProcess.WaitForExit();
          // 执行完成删除.uvms文件
          //File.Delete(uvmsOutputFilePath);
          Console.WriteLine("compile dllFilepath to uvm bytecode done");

          if (generateGpcFiles)
          {
            // -gpc选项
            var packageGpcProcess = new Process();
            var packageGpcFilePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "package_gpc"));
            packageGpcProcess.StartInfo.FileName = packageGpcFilePath;
            if (isWindows)
            {
              packageGpcProcess.StartInfo.FileName += ".exe";
            }
            packageGpcProcess.StartInfo.UseShellExecute = false;
            packageGpcProcess.StartInfo.CreateNoWindow = true;
            var bytecodeFilePath = Path.Combine(Path.GetDirectoryName(uvmsOutputFilePath), Path.GetFileNameWithoutExtension(uvmsOutputFilePath) + ".out");
            var contractMetaJsonFilePath = Path.Combine(Path.GetDirectoryName(uvmsOutputFilePath), Path.GetFileNameWithoutExtension(uvmsOutputFilePath) + ".meta.json");
            packageGpcProcess.StartInfo.Arguments = " " + bytecodeFilePath + " " + contractMetaJsonFilePath;
            packageGpcProcess.Start();
            packageGpcProcess.WaitForExit();
          }
        }
      }
      Console.WriteLine("process done");
    }
  }
}
