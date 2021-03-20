using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;


public static partial class TypeHelpers {

    /// <summary>
    /// 获取 LUA 的 字段 默认值
    /// </summary>
    public static string _GetDefaultValueDecl_Lua(this FieldInfo f, object o) {
        var t = f.FieldType;
        if (t._IsWeak() || t._IsShared()) {
            return "null";
        }
        if (t._IsClass() || t._IsStruct()) {
            return t._GetTypeDecl_Lua() + ".Create()";
        }
        if (t._IsData()) {
            return "NewXxData()";
        }
        if (t._IsList()) {
            return "{}";
        }

        var v = f.GetValue(o);
        if (t._IsString()) {
            return v == null ? "\"\"" : ("\"" + ((string)v).Replace("\"", "\"\"") + "\"");
        }
        if (t._IsNullable() || t._IsNumeric()) {
            return v == null ? "null" : v.ToString().ToLower();
        }
        if (t.IsEnum) {
            var sv = v._GetEnumInteger(t);
            // 如果 v 的值在枚举中找不到, 输出数字
            var fs = t._GetEnumFields();
            if (fs.Exists(f => f._GetEnumValue(t).ToString() == sv)) {
                return t._GetTypeDecl_Lua() + "." + v.ToString();
            }
            else {
                return v._GetEnumInteger(t).ToString();
            }
        }

        throw new Exception("unsupported type: " + t.FullName + " " + f.Name + " in " + f.DeclaringType.FullName);
    }

    // todo: 遇到 Shared 去壳, 遇到不包 Shared 的 class 报错

    /// <summary>
    /// 获取 LUA 的类型声明串
    /// </summary>
    public static string _GetTypeDecl_Lua(this Type t) {
        if (t._IsNullable()) {
            return "Nullable_" + _GetTypeDecl_Lua(t.GenericTypeArguments[0]);
        }
        else if (t._IsWeak()) {
            return "Weak_" + _GetTypeDecl_Lua(t.GenericTypeArguments[0]);
        }
        else if (t._IsList()) {
            string rtv = t.Name.Substring(0, t.Name.IndexOf('`')) + "_";
            for (int i = 0; i < t.GenericTypeArguments.Length; ++i) {
                if (i > 0)
                    rtv += "_";
                rtv += _GetTypeDecl_Lua(t.GenericTypeArguments[i]);
            }
            rtv += "_";
            return rtv;
        }
        else if (t.Namespace == nameof(System) || t.Namespace == nameof(TemplateLibrary)) {
            return t.Name;
        }
        return t.FullName.Replace(".", "_");
    }

    /// <summary>
    /// 获取 LUA 的 field type 备注
    /// </summary>
    public static string _GetTypeDesc_Lua(this Type t) {
        if (t._IsNullable()) {
            return "Nullable<" + _GetTypeDesc_Lua(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsData()) {
            return "XxData";
        }
        else if (t._IsWeak()) {
            return "Weak<" + _GetTypeDesc_Lua(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsShared()) {
            return "Shared<" + _GetTypeDesc_Lua(t.GenericTypeArguments[0]) + ">";
        }
        else if (t._IsList()) {
            return "List<" + _GetTypeDesc_Lua(t.GenericTypeArguments[0]) + ">";
        }
        else if (t.Namespace == nameof(System) || t.Namespace == nameof(TemplateLibrary)) {
            return t.Name;
        }
        return t.FullName;
    }

    public static string _GetReadCode_Lua(this Type t, string varName) {
        if (t._IsData()) {
            return "r, " + varName + " = om:Rdata()";
        }
        else if (t._IsString()) {
            return "r, " + varName + " = om:Rstr()";
        }
        else if (t._IsNumeric() || t.IsEnum) {
            return "r, " + varName + " = d:R" + t._GetRWFuncName_Lua() + "()";
        }
        else if (t._IsWeak() || t._IsShared()) {
            return "r, " + varName + " = om:Read()";
        }
        else if (t._IsClass() || t._IsStruct()) {
            return varName + @" = " + t._GetTypeDesc_Lua() + @".Create(); r = " + varName + ":Read(om)";
        }
        else if (t._IsNullable()) {
            var bak = t;
            t = t._GetChildType();
            if (t._IsData()) {
                return "r, " + varName + " = d:Rndata()";
            }
            else if (t._IsString()) {
                return "r, " + varName + " = om:Rnstr()";
            }
            else if (t._IsNumeric() || t.IsEnum) {
                return "r, " + varName + " = d:Rn" + t._GetRWFuncName_Lua() + "()";
            }
            else if (t._IsClass() || t._IsStruct()) {
                return "r, o = d:Ru8(); if r ~= 0 then return r end; if o == 0 then " + varName + " = null else " + varName + @" = " + t._GetTypeDesc_Lua() + @".Create(); r = " + varName + ":Read(om) end";
            }
            else
                throw new System.Exception("unsupported type: " + bak.FullName);
        }
        throw new Exception("unsupported type");
    }

    /// <summary>
    /// 获取 field read write 的 d:R/W ??? 部分. 仅针对 Numeric & Enum
    /// </summary>
    public static string _GetRWFuncName_Lua(this Type t) {
        if (t._IsNumeric()) {
            switch (t.Name) {
                case "Byte":
                    return "u8";
                case "UInt8":
                    return "u8";
                case "UInt16":
                    return "u16";
                case "UInt32":
                    return "u32";
                case "UInt64":
                    return "u64";
                case "SByte":
                    return "i8";
                case "Int8":
                    return "i8";
                case "Int16":
                    return "i16";
                case "Int32":
                    return "i32";
                case "Int64":
                    return "i64";
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
                    return "u16";
                case "Int16":
                    return "i16";
                case "UInt32":
                    return "u32";
                case "Int32":
                    return "i32";
                case "UInt64":
                    return "u64";
                case "Int64":
                    return "i64";
            }
        }
        throw new Exception("unsupported type");
    }

    /// <summary>
    /// 获取 LUA 风格的注释
    /// </summary>
    public static string _GetComment_Lua(this string s, int space) {
        if (s.Trim() == "")
            return "";
        var sps = new string(' ', space);
        return @"
" + sps + @"--[[
" + sps + s + @"
" + sps + "]]";
    }
}
