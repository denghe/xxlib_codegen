﻿using System;
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
            return "Nullable" + _GetTypeDecl_Lua(t.GenericTypeArguments[0]);
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
        return (t._IsExternal() ? "" : "") + "_" + t.FullName.Replace(".", "_");
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
