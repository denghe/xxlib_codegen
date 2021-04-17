using System;

public static partial class TypeHelpers
{
    /// <summary>
    ///  获取RUST 命名规则
    /// </summary>   
    public static string _GetTypeName_Rust(this Type t)
    {
        return t.FullName.Replace('.', '_');
    }

    /// <summary>
    /// 获取RUST 枚举类型
    /// </summary>
    public static string _GetEnumUnderlyingTypeName_Rust(this Type e)
    {
        switch (e.GetEnumUnderlyingType().Name)
        {
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
        throw new Exception("unsupported data type");
    }

    /// <summary>
    /// 获取 RUST 风格的注释
    /// </summary>
    public static string _GetComment_Rust(this string s, int space)
    {
        if (s.Trim() == "")
            return "";
        var sps = new string(' ', space);
        s = s.Replace("\r\n", "\n")
         .Replace("\r", "\n")
         .Replace("\n", "\r\n" + sps + "/// ");
        return "\r\n" +sps + @"/// " + s;

    }

    /// <summary>
    /// 获取 RUST  的默认值填充代码
    /// </summary>
    public static string _GetDefaultValueDecl_Rust(this Type t, object v)
    {
        if (t._IsNullable())
        {
            return v == null ? "" :$"Some({v})";
        }
        if (t.IsGenericType || t._IsData())
        {
            return "";
        }
        if (t._IsString())
        {
            return v == null ? "" : ("\"" + ((string)v).Replace("\"", "\"\"") + "\"");
        }
        if (t.IsValueType)
        {
            if (t.IsEnum)
            {
                var sv = v._GetEnumInteger(t);
                if (sv == "0") return "";

                // 如果 v 的值在枚举中找不到, 输出硬转格式. 否则输出枚举项
                var fs = t._GetEnumFields();
                if (fs.Exists(f => f._GetEnumValue(t).ToString() == sv))
                {
                    return _GetTypeName_Rust(t) + "::" + v.ToString();
                }
                else
                {
                    return "";
                }
            }
            if (t._IsNumeric())
            {
                if (v.ToString() == "0")
                    return "";

                if (t.Name == "Single"||t.Name== "Double")
                {
                    var s = v.ToString().ToLower();
                    if (s.Contains(".")) return s;
                    return s + ".0";
                }
                else
                    return v.ToString().ToLower();   // lower for Ture, False bool
            }
            else return "";
        }
        // class?
        return "";
    }

    /// <summary>
    /// 获取 C# 的类型声明串
    /// </summary>
    public static string _GetStructTypeBase_Rust(this Type t)
    {
        if (t._IsNullable())
        {
           return  t._GetTypeDecl_Rust();
        }
        if (t.IsArray)
        {
            return t._GetTypeDecl_Rust();
        }
        else if (t._IsTuple())
        {
            return t._GetTypeDecl_Rust();
        }
        else if (t.IsEnum)
        {
            return t._GetTypeDecl_Rust();
        }
        else
        {
            if (t.Namespace == nameof(TemplateLibrary))
            {
                throw new NotSupportedException();
            }
            else if (t.Namespace == nameof(System))
            {
                switch (t.Name)
                {
                    case "ValueType":
                    case "Object":
                    case "Enum":
                        return "";
                    default:
                        throw new NotSupportedException();
                }
            }

            return t._GetTypeDecl_Rust();          
        }
    }

    /// <summary>
    /// 获取RUST 类型定义
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public static string _GetTypeDecl_Rust(this Type t)
    {
        if (t._IsNullable())
        {
            return "Option<" + t.GenericTypeArguments[0]._GetTypeDecl_Rust() + ">";
        }
        if (t._IsData())
        {
            return "Vec<u8>";
        }
        else if (t._IsTuple())
        {
            string rtv = "(";
            for (int i = 0; i < t.GenericTypeArguments.Length; ++i)
            {
                if (i > 0)
                {
                    rtv += ", ";
                }
                rtv += t.GenericTypeArguments[i]._GetTypeDecl_Rust();
            }
            rtv += ")";
            return rtv;
        }
        else if (t.IsEnum)  // enum & struct
        {
            return t._GetTypeName_Rust();
        }
        else
        {
            if (t.Namespace == nameof(TemplateLibrary))
            {
                switch (t.Name)
                {
                    case "Weak`1":
                        return "Weak<" + _GetTypeDecl_Rust(t.GenericTypeArguments[0]) + ">";
                    case "Shared`1":
                        return "SharedPtr<" + _GetTypeDecl_Rust(t.GenericTypeArguments[0]) + ">";
                    case "Unique`1":
                        throw new NotImplementedException("Unique ptr");
                    case "List`1":
                        {
                            var ct = t.GenericTypeArguments[0];
                            return "Vec<" + ct._GetTypeDecl_Rust() + ">";
                        }
                    case "Dict`2":
                        {
                            return "std::collections::BTreeMap<" + t.GenericTypeArguments[0]._GetTypeDecl_Rust() + ", " + t.GenericTypeArguments[1]._GetTypeDecl_Rust() + ">";
                        }
                    case "HashMap`2":
                        {
                            return "std::collections::HashMap<" + t.GenericTypeArguments[0]._GetTypeDecl_Rust() + ", " + t.GenericTypeArguments[1]._GetTypeDecl_Rust() + ">";
                        }
                    case "HashSet`1":
                        {
                            return "std::collections::HashSet<" + t.GenericTypeArguments[0]._GetTypeDecl_Rust()+">";
                        }
                    case "Pair`2":
                        {
                            return "(" + t.GenericTypeArguments[0]._GetTypeDecl_Rust() + ", " + t.GenericTypeArguments[1]._GetTypeDecl_Rust() + ")";
                        }
                    case "Data":
                        return "Vec<u8>";
                    default:
                        throw new NotImplementedException();
                }
            }
            else if (t.Namespace == nameof(System))
            {
                switch (t.Name)
                {
                    case "Object":
                        throw new NotImplementedException("Object");
                    case "Void":
                        throw new NotImplementedException("Void");
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
                        return "f64";
                    case "Float":
                        return "f32";
                    case "Single":
                        return "f32";
                    case "Boolean":
                        return "bool";
                    case "Bool":
                        return "bool";
                    case "String":
                        return "String";
                }
            }
            return t._GetTypeName_Rust();
        }
    }

}
