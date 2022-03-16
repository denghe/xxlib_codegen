using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;


public static partial class TypeHelpers {

    /// <summary>
    /// 获取 JS 的 字段 默认值
    /// </summary>
    public static string _GetDefaultValueDecl_Js(this FieldInfo f, object o) {
        var t = f.FieldType;
        if (t._IsWeak() || t._IsShared()) {
            return "null";
        }
        if (t._IsClass() || t._IsStruct()) {
            return "new " + t._GetTypeDecl_Js() + "()";
        }
        if (t._IsData()) {
            return "new Data()";
        }
        if (t._IsList()) {
            return "[]";
        }

        var v = f.GetValue(o);
        if (t._IsString()) {
            return v == null ? "\"\"" : ("\"" + ((string)v).Replace("\"", "\"\"") + "\"");
        }
        if (t._IsNullable() || t._IsNumeric()) {
            return v == null ? "null" : (v.ToString().ToLower() + (t._IsIntUint64() ? "n" : ""));
        }
        if (t.IsEnum) {
            var sv = v._GetEnumInteger(t);
            // 如果 v 的值在枚举中找不到, 输出数字
            var fs = t._GetEnumFields();
            if (fs.Exists(f => f._GetEnumValue(t).ToString() == sv)) {
                return t._GetTypeDecl_Js() + "." + v.ToString();
            }
            else {
                return v._GetEnumInteger(t).ToString();
            }
        }

        throw new Exception("unsupported type: " + t.FullName + " " + f.Name + " in " + f.DeclaringType.FullName);
    }

    /// <summary>
    /// 获取 Js 的类型声明串
    /// </summary>
    public static string _GetTypeDecl_Js(this Type t) {
        return t.FullName.Replace(".", "_");
    }

    /// <summary>
    /// 获取 Js 的 field type 备注
    /// </summary>
    public static string _GetTypeDesc_Js(this Type t) {
        if (t._IsNullable()) {
            return "Nullable<" + _GetTypeDesc_Js(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsData()) {
            return "XxData";
        }
        else if (t._IsWeak()) {
            return "Weak<" + _GetTypeDesc_Js(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsShared()) {
            return "Shared<" + _GetTypeDesc_Js(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsList()) {
            return "List<" + _GetTypeDesc_Js(t.GenericTypeArguments[0]) + ">";
        }
        else if (t.Namespace == nameof(System) || t.Namespace == nameof(TemplateLibrary)) {
            return t.Name;
        }
        return t.FullName;
    }

    public static string _GetReadCode_Js(this Type t, string varName, string owner = "this.") {
        if (t._IsData()) {
            return owner + varName + " = d.Rdata();";
        }
        else if (t._IsString()) {
            return owner + varName + " = d.Rstr();";
        }
        else if (t._IsNumeric() || t.IsEnum) {
            return owner + varName + " = d.R" + t._GetRWFuncName_Js() + "();";
        }
        else if (t._IsWeak() || t._IsShared()) {
            return owner + varName + " = d.Read();";
        }
        else if (t._IsClass() || t._IsStruct()) {
            return owner + varName + @".Read(d);";
        }
        else if (t._IsNullable()) {
            var bak = t;
            t = t._GetChildType();
            var s = "let n = d.Ru8(); if (n == 0) " + owner + varName + " = null; else { ";
            if (t._IsData()) {
                s += owner + varName + " = d.Rdata(); }";
            }
            else if (t._IsString()) {
                s+= owner + varName + " = d.Rstr(); }";
            }
            else if (t._IsNumeric() || t.IsEnum) {
                s += owner + varName + " = d.R" + t._GetRWFuncName_Js() + "(); }";
            }
            else if (t._IsClass() || t._IsStruct()) {
                s += owner + varName + @" = new " + t._GetTypeDecl_Js() + @"(); " + owner + varName + @".Read(d); }";
            }
            else
                throw new System.Exception("unsupported type: " + bak.FullName);
            return s;
        }
        throw new Exception("unsupported type");
    }

    public static string _GetWriteCode_Js(this Type t, string varName, string owner = "this.") {
        if (t._IsData()) {
            return "d.Wdata(" + owner + varName + ");";
        }
        else if (t._IsString()) {
            return "d.Wstr(" + owner + varName + ");";
        }
        else if (t._IsNumeric() || t.IsEnum) {
            return "d.W" + t._GetRWFuncName_Js() + "(" + owner + varName + ");";
        }
        else if (t._IsWeak() || t._IsShared()) {
            return "d.Write(" + owner + varName + ");";
        }
        else if (t._IsClass() || t._IsStruct()) {
            return owner + varName + @".Write(d);";
        }
        else if (t._IsNullable()) {
            var bak = t;
            var s = "if (" + owner + varName + " === null || " + owner + varName + " === undefined) d.Wu8(0); else { d.Wu8(1); ";
            t = t._GetChildType();
            if (t._IsData()) {
                s += "d.Wdata(" + owner + varName + "); }";
            }
            else if (t._IsString()) {
                s += "d.Wstr(" + owner + varName + "); }";
            }
            else if (t._IsNumeric() || t.IsEnum) {
                s += "d.W" + t._GetRWFuncName_Js() + "(" + owner + varName + "); }";
            }
            else if (t._IsClass() || t._IsStruct()) {
                s += owner + varName + ".Write(d); }";
            }
            else
                throw new System.Exception("unsupported type: " + bak.FullName);
            return s;
        }
        throw new Exception("unsupported type");
    }

    /// <summary>
    /// 获取 field read write 的 d.R/W ??? 部分. 仅针对 Numeric & Enum
    /// </summary>
    public static string _GetRWFuncName_Js(this Type t) {
        if (t._IsNumeric()) {
            switch (t.Name) {
                case "Byte":
                    return "u8";
                case "UInt8":
                    return "u8";
                case "UInt16":
                    return "vu";
                case "UInt32":
                    return "vu";
                case "UInt64":
                    return "vu64";
                case "SByte":
                    return "i8";
                case "Int8":
                    return "i8";
                case "Int16":
                    return "vi";
                case "Int32":
                    return "vi";
                case "Int64":
                    return "vi64";
                case "Double":
                    return "d";
                case "Float":
                    return "f";
                case "Single":
                    return "f";
                case "Boolean":
                    return "b";
                case "Bool":
                    return "b";
            }
        }
        else if (t.IsEnum) {
            switch (t.GetEnumUnderlyingType().Name) {
                case "Byte":
                    return "u8";
                case "SByte":
                    return "i8";
                case "UInt16":
                    return "vu";
                case "Int16":
                    return "vi";
                case "UInt32":
                    return "vu";
                case "Int32":
                    return "vi";
                case "UInt64":
                    return "vu64";
                case "Int64":
                    return "vi64";
            }
        }
        throw new Exception("unsupported type");
    }

    /// <summary>
    /// 获取 Js 风格的注释
    /// </summary>
    public static string _GetComment_Js(this string s, int space) {
        if (s.Trim() == "")
            return "";
        var sps = new string(' ', space);
        s = s.Replace("\r\n", "\n")
         .Replace("\r", "\n")
         .Replace("\n", "\r\n" + sps + "// ");
        return "\r\n"
 + sps + @"// " + s;
    }
}
