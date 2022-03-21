using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Linq;
using System.Collections.Generic;

public static class Program {

    public static void TipsAndExit(string msg, int exitCode = 0) {
        Console.WriteLine(msg);
        if (exitCode != 0) {
            Console.WriteLine("按任意键退出");
            Console.ReadKey();
        }
        Environment.Exit(exitCode);
    }

    public static void Main(string[] args) {
        if (args.Length == 0) {
            Console.WriteLine("codegen need args: cfg file names");
        }
        else {
            foreach(var a in args) {
                // file -> cfg instance
                TypeHelpers.cfg = Cfg.ReadFrom(a);

                Console.WriteLine("开始生成");
                try {
                    if (!string.IsNullOrWhiteSpace(TypeHelpers.cfg.outdir_cpp)) {
                        GenCpp.Gen();
                    }
                    if (!string.IsNullOrWhiteSpace(TypeHelpers.cfg.outdir_cs)) {
                        GenCS.Gen();
                    }
                    if (!string.IsNullOrWhiteSpace(TypeHelpers.cfg.outdir_lua)) {
                        GenLua.Gen();
                    }
                    if (!string.IsNullOrWhiteSpace(TypeHelpers.cfg.outdir_rs))
                    {
                        GenRust.Gen();
                    }
                    if (!string.IsNullOrWhiteSpace(TypeHelpers.cfg.outdir_js))
                    {
                        GenJs.Gen();
                        GenTs.Gen();
                    }
                }
                catch (Exception ex) {
                    TipsAndExit("生成失败: " + ex.Message + "\r\n" + ex.StackTrace, -1);
                }

            }
        }
        TipsAndExit("生成完毕");
    }
}
