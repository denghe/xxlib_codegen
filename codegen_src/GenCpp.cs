using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;

// 基于 xx_obj.h
// 针对 Shared<T>, 生成 xx::Shared<T>. 要求每个用于智能指针的类 标注 [TypeId]. 检查到漏标就报错
// 针对 Weak<T> 类成员属性, 生成 xx::Weak<T>
// 针对 struct 或标记有 [Struct] 的 class: 生成 ObjFuncs 模板特化适配

partial class Info {
    // 简单类型：ObjManager 写 Data 时不需要分析指针递归引用关系
    public bool? _isSimpleType;
    public bool isSimpleType {
        get {
            return _isSimpleType ?? false;
        }
    }
    public static bool CheckIsSimpleType(Type t) {
        if (t._IsExternal()) return t.IsValueType;
        if (t.IsEnum || t._IsNumeric() || t._IsString() || t._IsData()) return true;
        if (t.IsGenericType) {
            foreach (var ct in t.GetGenericArguments()) {
                if (!CheckIsSimpleType(ct)) return false;
            }
            return true;
        }
        if (t._IsClass() || t._IsStruct()) {
            var ti = t._GetInfo();
            if (ti._isSimpleType.HasValue) return ti._isSimpleType.Value;
            if (t._IsClass() && t._HasDerives()) return false;
            var fs = t._GetExtractFields();
            foreach (var f in fs) {
                if (!CheckIsSimpleType(f.FieldType)) {
                    ti._isSimpleType = false;
                    return false;
                }
            }
            ti._isSimpleType = true;
            return true;
        }
        if (t._IsWeak() || t._IsShared()) return false; // todo: 以后再进一步检查是否存在递归可能，如果不可能则似乎也能走简化读写
        throw new Exception("not impl?" + t.Name);
    }

    // 检查 type 是否为 “简单类型” ( 只含有 数值,枚举,string 或套List 的类型 )
    public void SetIsSimpleType() {
        if (this._isSimpleType.HasValue) return;
        this._isSimpleType = CheckIsSimpleType(this.type);
    }

    // 单纯类型：写 Data 时全程不需要用到 ObjManager, 且没有打开兼容模式( 成员只能是简单类型，可嵌套结构体和泛型，不可出现 类 )
    public bool? _isPureType;
    public bool isPureType {
        get {
            return _isPureType ?? false;
        }
    }
    public static bool CheckIsPureType(Type t, bool isRoot = true) {
        if (t._IsExternal()) return t.IsValueType;
        if (t.IsEnum || t._IsNumeric() || t._IsString() || t._IsData()) return true;
        if (t.IsGenericType) {
            foreach (var ct in t.GetGenericArguments()) {
                if (!CheckIsPureType(ct, false)) return false;
            }
            return true;
        }
        if (t._IsStruct()) {
            var ti = t._GetInfo();
            if (ti._isPureType.HasValue) return ti._isPureType.Value;
            if (t._HasCompatible()) return false;
            var fs = t._GetExtractFields();
            foreach (var f in fs) {
                if (!CheckIsPureType(f.FieldType, false)) {
                    ti._isPureType = false;
                    return false;
                }
            }
            ti._isPureType = true;
            return true;
        }
        if (t._IsClass()) return isRoot;
        throw new Exception("not impl? " + t.Name);
    }

    // 检查 type 是否为 “简单类型” ( 只含有 数值,枚举,string 或套List 的类型 )
    public void SetIsPureType() {
        if (this._isPureType.HasValue) return;
        this._isPureType = CheckIsPureType(this.type);
    }
}

public static class GenCpp {
    public static bool _IsSimpleType(this Type t) {
        var i = t._GetInfo();
        if (i == null) return true;
        return i._isSimpleType.Value;
    }

    public static bool _IsPureType(this Type t) {
        var i = t._GetInfo();
        if (i == null) return true;
        return i._isPureType.Value;
    }

    // 简化传参
    static Cfg cfg;
    static List<string> createEmptyFiles = new List<string>();

    public static void Gen() {
        cfg = TypeHelpers.cfg;
        createEmptyFiles.Clear();
        foreach (var c in cfg.typeInfos) {
            c.Value.SetIsSimpleType();
            c.Value.SetIsPureType();
        }

        Gen_h();
        Gen_cpp();
        Gen_ajson_h();
        Gen_empties();
    }

    public static void Gen_h() {
        var sb = new StringBuilder();
        sb.Append(@"#pragma once");

        // 包含依赖
        if (cfg.refsCfgs.Count == 0) {
            sb.Append(@"
#include ""xx_obj.h""");
        }
        foreach (var c in cfg.refsCfgs) {
            sb.Append(@"
#include """ + c.name + @".h""");
        }

        // 前置切片
        createEmptyFiles.Add(cfg.name + ".h.inc");
        sb.Append(@"
#include """ + cfg.name + @".h.inc""");

        // 校验和注册
        sb.Append(@"
struct CodeGen_" + cfg.name + @" {
	inline static const ::std::string md5 = """ + StringHelpers.MD5PlaceHolder + @""";
    static void Register();
    CodeGen_" + cfg.name + @"() { Register(); }
};
inline CodeGen_" + cfg.name + @" __CodeGen_" + cfg.name + @";");

        // 所有 本地 class 的预声明
        foreach (var c in cfg.localClasss) {
            var ns = c._GetNamespace_Cpp(false);
            if (string.IsNullOrEmpty(ns)) {
                sb.Append(@"
struct " + c.Name + ";");
            }
            else sb.Append(@"
namespace " + c._GetNamespace_Cpp(false) + @" { struct " + c.Name + "; }");
        }

        // 所有 本地 class 的 TypeId 映射
        if (cfg.localClasss.Count > 0) {
            sb.Append(@"
namespace xx {");
            foreach (var c in cfg.localClasss) {
                sb.Append(@"
    template<> struct TypeId<" + c._GetTypeDecl_Cpp() + @"> { static const uint16_t value = " + c._GetTypeId() + @"; };");
            }
            sb.Append(@"
}
");
        }

        // 所有 本地 enums
        foreach (var e in cfg.localEnums) {
            var ns = e._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
namespace " + ns + "{");
                ss = "    ";
            }

            sb.Append(e._GetDesc()._GetComment_Cpp(4) + @"
" + ss + @"    enum class " + e.Name + @" : " + e._GetEnumUnderlyingTypeName_Cpp() + @" {");

            var fs = e._GetEnumFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Cpp(8 - ss.Length) + @"
" + ss + @"        " + f.Name + " = " + f._GetEnumValue(e) + ",");
            }

            sb.Append(@"
" + ss + @"    };");

            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
}");
            }
        }

        var a = new Action<Type>((c) => {
            var o = c._GetInstance();
            var ns = c._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
namespace " + ns + @" {");
                ss = "    ";
            }

            // 头部
            var bt = c.BaseType;
            if (c._IsStruct()) {
                var btn = c._HasBaseType() ? (" : " + bt._GetTypeDecl_Cpp()) : "";
                sb.Append(c._GetDesc()._GetComment_Cpp(ss.Length) + @"
" + ss + @"struct " + c.Name + btn + @" {
" + ss + @"    XX_OBJ_STRUCT_H(" + c.Name + @")");
            }
            else {
                var btn = c._HasBaseType() ? bt._GetTypeDecl_Cpp() : "::xx::ObjBase";
                sb.Append(c._GetDesc()._GetComment_Cpp(ss.Length) + @"
" + ss + @"struct " + c.Name + " : " + btn + @" {
" + ss + @"    XX_OBJ_OBJECT_H(" + c.Name + @", " + btn + @")");
            }

            // 附加标签
            if (c._GetInfo().isSimpleType) {
                sb.Append(@"
" + ss + @"    using IsSimpleType_v = " + c.Name + ";");
            }

            // 前置包含
            if (c._HasInclude()) {
                var fn = c._GetUnderlineFullname() + ".inc";
                createEmptyFiles.Add(fn);
                sb.Append(@"
#include """ + fn + @"""");
            }

            // 成员
            foreach (var f in c._GetFieldsConsts()) {
                var ft = f.FieldType;
                var ftn = ft._GetTypeDecl_Cpp();
                sb.Append(f._GetDesc()._GetComment_Cpp(8) + @"
" + ss + @"    " + (f.IsStatic ? "constexpr " : "") + ftn + " " + f.Name);

                var v = f.GetValue(f.IsStatic ? null : o);
                var dv = ft._GetDefaultValueDecl_Cpp(v);
                if (dv != "") {
                    sb.Append(" = " + dv + ";");
                }
                else {
                    sb.Append(";");
                }
            }

            // WriteTo
            if (c._IsPureType()) {
                sb.Append(@"
" + ss + @"    static void WriteTo(xx::Data& d");
                foreach (var f in c._GetExtractFields()) {
                    sb.Append(", " + f.FieldType._GetTypeDecl_Cpp() + " const&");
                }
                sb.Append(");");
            }

            // 后置包含
            if (c._HasInclude_()) {
                var fn = c._GetUnderlineFullname() + "_.inc";
                createEmptyFiles.Add(fn);
                sb.Append(@"
#include """ + fn + @"""");
            }

            sb.Append(@"
" + ss + @"};");

            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
}");
            }
        });

        // 所有本地 structs & class
        foreach (var c in cfg.localStructs) {
            a(c);
        }
        foreach (var c in cfg.localClasss) {
            a(c);
        }
        if (cfg.localStructs.Count > 0) {
            sb.Append(@"
namespace xx {");
            foreach (var c in cfg.localStructs) {
                sb.Append(@"
	XX_OBJ_STRUCT_TEMPLATE_H(" + c._GetTypeDecl_Cpp() + @")");
                if (c._IsPureType()) {
                    sb.Append(@"
    template<typename T> struct DataFuncs<T, std::enable_if_t<std::is_same_v<" + c._GetTypeDecl_Cpp() + @", std::decay_t<T>>>> {
		template<bool needReserve = true>
		static inline void Write(Data& d, T const& in) { (*(xx::ObjManager*)nullptr).Write(d, in); }
		static inline int Read(Data_r& d, T& out) { return (*(xx::ObjManager*)nullptr).Read(d, out); }
    };");
                }
            }
            sb.Append(@"
}");
        }

        // 后置切片
        {
            var fn = cfg.name + "_.h.inc";
            createEmptyFiles.Add(fn);
            sb.Append(@"
#include """ + fn + @"""
");
        }

        sb._WriteToFile(Path.Combine(cfg.outdir_cpp, cfg.name + ".h"));
    }

    public static void Gen_cpp() {
        var sb = new StringBuilder();
        // 前置包含
        {
            var fn = cfg.name + ".cpp.inc";
            sb.Append("#include \"" + cfg.name + @".h""
#include """ + fn + @"""");
            createEmptyFiles.Add(fn);
        }

        // type id 注册
        sb.Append(@"
void CodeGen_" + cfg.name + @"::Register() {");
        foreach (var c in cfg.localClasss) {
            sb.Append(@"
	::xx::ObjManager::Register<" + c._GetTypeDecl_Cpp() + @">();");
        }
        sb.Append(@"
}");

        // 模板适配
        if (cfg.localStructs.Count > 0) {
            sb.Append(@"
namespace xx {");
        }
        foreach (var c in cfg.localStructs) {
            var o = c._GetInstance();

            var ctn = c._GetTypeDecl_Cpp();
            var fs = c._GetFields();

            // Write
            sb.Append(@"
	void ObjFuncs<" + ctn + @", void>::Write(::xx::ObjManager& om, ::xx::Data& d, " + ctn + @" const& in) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        ObjFuncs<" + btn + ">::Write(om, d, in);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        auto bak = d.WriteJump(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType()) {
                    sb.Append(@"
        d.Write(in." + f.Name + ");");
                }
                else {
                    sb.Append(@"
        om.Write(d, in." + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        d.WriteFixedAt(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
    }");

            // WriteFast
            sb.Append(@"
	void ObjFuncs<" + ctn + @", void>::WriteFast(::xx::ObjManager& om, ::xx::Data& d, " + ctn + @" const& in) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        ObjFuncs<" + btn + ">::Write<false>(om, d, in);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        auto bak = d.WriteJump<false>(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType()) {
                    sb.Append(@"
        d.Write<false>(in." + f.Name + ");");
                }
                else {
                    sb.Append(@"
        om.Write<false>(d, in." + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        d.WriteFixedAt<false>(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
    }");

            // Read
            sb.Append(@"
	int ObjFuncs<" + ctn + @", void>::Read(::xx::ObjManager& om, ::xx::Data_r& d, " + ctn + @"& out) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();

                sb.Append(@"
        if (int r = ObjFuncs<" + btn + ">::Read(om, d, out)) return r;");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        uint32_t siz;
        if (int r = d.ReadFixed(siz)) return r;
        if (siz < sizeof(siz)) return __LINE__;
        siz -= sizeof(siz);
        if (siz > d.len - d.offset) return __LINE__;
        xx::Data_r dr(d.buf + d.offset, siz);
");
                foreach (var f in fs) {
                    var ft = f.FieldType;

                    string dv = "";
                    var v = f.GetValue(f.IsStatic ? null : o);
                    dv = ft._GetDefaultValueDecl_Cpp(v);
                    if (dv != "") {
                        dv = "out." + f.Name + " = " + dv;
                    }
                    else {
                        dv = "om.SetDefaultValue(out." + f.Name + ")";
                    }

                    sb.Append(@"
        if (dr.offset == siz) " + dv + @";
        else if (int r = om.Read(dr, out." + f.Name + @")) return r;");
                }

                sb.Append(@"

        d.offset += siz;");

            }
            else {
                foreach (var f in fs) {
                    if (f.FieldType._IsPureType()) {
                        sb.Append(@"
        if (int r = d.Read(out." + f.Name + @")) return r;");
                    }
                    else {
                        sb.Append(@"
        if (int r = om.Read(d, out." + f.Name + @")) return r;");
                    }
                }
            }

            sb.Append(@"
        return 0;
    }");

            // Append
            sb.Append(@"
	void ObjFuncs<" + ctn + @", void>::Append(ObjManager &om, std::string& s, " + ctn + @" const& in) {
#ifndef XX_DISABLE_APPEND
        s.push_back('{');
        AppendCore(om, s, in);
        s.push_back('}');
#endif
    }
	void ObjFuncs<" + ctn + @", void>::AppendCore(ObjManager &om, std::string& s, " + ctn + @" const& in) {
#ifndef XX_DISABLE_APPEND");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        auto sizeBak = s.size();
        ObjFuncs<" + btn + ">::AppendCore(om, s, in);");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (f == fs[0]) {
                    if (c._HasBaseType()) {
                        sb.Append(@"
        if (sizeBak < s.size()) {
            s.push_back(',');
        }");
                    }
                    sb.Append(@"
        om.Append(s, ""\""" + f.Name + @"\"":"", in." + f.Name + @"); ");
                }
                else {
                    sb.Append(@"
        om.Append(s, "",\""" + f.Name + @"\"":"", in." + f.Name + @");");
                }
            }
            sb.Append(@"
#endif
    }");

            // Clone
            sb.Append(@"
    void ObjFuncs<" + ctn + @">::Clone(::xx::ObjManager& om, " + ctn + @" const& in, " + ctn + @" &out) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        ObjFuncs<" + btn + ">::Clone_(om, in, out);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
        om.Clone_(in." + f.Name + ", out." + f.Name + ");");
            }
            sb.Append(@"
    }");

            // RecursiveCheck
            sb.Append(@"
    int ObjFuncs<" + ctn + @">::RecursiveCheck(::xx::ObjManager& om, " + ctn + @" const& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        if (int r = ObjFuncs<" + btn + ">::RecursiveCheck(om, in)) return r;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
        if (int r = om.RecursiveCheck(in." + f.Name + ")) return r;");
            }
            sb.Append(@"
        return 0;
    }");

            // RecursiveReset
            sb.Append(@"
    void ObjFuncs<" + ctn + @">::RecursiveReset(::xx::ObjManager& om, " + ctn + @"& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        ObjFuncs<" + btn + ">::RecursiveReset(om, in);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
        om.RecursiveReset(in." + f.Name + ");");
            }
            sb.Append(@"
    }");

            // SetDefaultValue
            sb.Append(@"
    void ObjFuncs<" + ctn + @">::SetDefaultValue(::xx::ObjManager& om, " + ctn + @"& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
        ObjFuncs<" + btn + ">::SetDefaultValue(om, in);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;

                string dv = "";
                var v = f.GetValue(f.IsStatic ? null : o);
                dv = ft._GetDefaultValueDecl_Cpp(v);
                if (dv != "") {
                    dv = "in." + f.Name + " = " + dv;
                }
                else {
                    dv = "om.SetDefaultValue(in." + f.Name + ")";
                }

                sb.Append(@"
        " + dv + @";");
            }
            sb.Append(@"
    }");
        }
        if (cfg.localStructs.Count > 0) {
            sb.Append(@"
}");
        }

        // class 成员函数
        foreach (var c in cfg.localClasss) {
            var ns = c._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
namespace " + ns + "{");
                ss = "    ";
            }

            var o = c._GetInstance();
            var fs = c._GetFields();

            // WriteTo
            if (c._IsPureType()) {
                sb.Append(@"
" + ss + @"void " + c.Name + @"::WriteTo(xx::Data& d");
                foreach (var f in c._GetExtractFields()) {
                    var ft = f.FieldType;
                    var ftn = ft._GetTypeDecl_Cpp();
                    if (ft._IsString()) {
                        ftn = "std::string_view";
                    }
                    sb.Append(", " + ftn + " const& " + f.Name);
                }
                sb.Append(@") {
" + ss + @"    d.Write(xx::TypeId_v<" + c.Name + ">);");
                foreach (var f in c._GetExtractFields()) {
                    sb.Append(@"
" + ss + @"    d.Write(" + f.Name + ");");
                }
                sb.Append(@"
" + ss + @"}");
            }

            // Write
            sb.Append(@"
" + ss + @"void " + c.Name + @"::Write(::xx::ObjManager& om, ::xx::Data& d) const {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    this->BaseType::Write(om, d);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
" + ss + @"    auto bak = d.WriteJump(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType()) {
                    sb.Append(@"
" + ss + @"    d.Write(this->" + f.Name + ");");
                }
                else {
                    sb.Append(@"
" + ss + @"    om.Write(d, this->" + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
" + ss + @"    d.WriteFixedAt(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
" + ss + @"}");

            // Read
            sb.Append(@"
" + ss + @"int " + c.Name + @"::Read(::xx::ObjManager& om, ::xx::Data_r& d) {");

            if (c._HasBaseType()) {
                sb.Append(@"
" + ss + @"    if (int r = this->BaseType::Read(om, d)) return r;");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
" + ss + @"    uint32_t siz;
" + ss + @"    if (int r = d.ReadFixed(siz)) return r;
" + ss + @"    if (siz < sizeof(siz)) return __LINE__;
" + ss + @"    siz -= sizeof(siz);
" + ss + @"    if (siz > d.len - d.offset) return __LINE__;
" + ss + @"    xx::Data_r dr(d.buf + d.offset, siz);
");
                foreach (var f in fs) {
                    var ft = f.FieldType;

                    string dv = "";
                    var v = f.GetValue(f.IsStatic ? null : o);
                    dv = ft._GetDefaultValueDecl_Cpp(v);
                    if (dv != "") {
                        dv = "this->" + f.Name + " = " + dv;
                    }
                    else {
                        dv = "om.SetDefaultValue(this->" + f.Name + ")";
                    }

                    sb.Append(@"
" + ss + @"    if (dr.offset == siz) " + dv + @";
" + ss + @"    else if (int r = om.Read(dr, this->" + f.Name + @")) return r;");
                }

                sb.Append(@"

" + ss + @"    d.offset += siz;");
            }
            else {
                foreach (var f in fs) {
                    if (f.FieldType._IsPureType()) {
                        sb.Append(@"
" + ss + @"    if (int r = d.Read(this->" + f.Name + @")) return r;");
                    }
                    else {
                        sb.Append(@"
" + ss + @"    if (int r = om.Read(d, this->" + f.Name + @")) return r;");
                    }
                }
            }

            sb.Append(@"
" + ss + @"    return 0;
" + ss + @"}");

            // Append
            sb.Append(@"
" + ss + @"void " + c.Name + @"::Append(::xx::ObjManager& om, std::string& s) const {
#ifndef XX_DISABLE_APPEND
" + ss + @"    ::xx::Append(s, ""{\""__typeId__\"":" + c._GetTypeId() + @""");");
            sb.Append(@"
" + ss + @"    this->AppendCore(om, s);
" + ss + @"    s.push_back('}');
#endif
" + ss + @"}
" + ss + @"void " + c.Name + @"::AppendCore(::xx::ObjManager& om, std::string& s) const {
#ifndef XX_DISABLE_APPEND");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    this->BaseType::AppendCore(om, s);");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
" + ss + @"    om.Append(s, "",\""" + f.Name + @"\"":"", this->" + f.Name + @");");
            }
            sb.Append(@"
#endif
" + ss + @"}");

            // Clone
            sb.Append(@"
" + ss + @"void " + c.Name + @"::Clone(::xx::ObjManager& om, void* const &tar) const {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    this->BaseType::Clone(om, tar);");
            }
            if (fs.Count > 0) {
                sb.Append(@"
" + ss + @"    auto out = (" + c._GetTypeDecl_Cpp() + @"*)tar;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
" + ss + @"    om.Clone_(this->" + f.Name + ", out->" + f.Name + ");");
            }
            sb.Append(@"
" + ss + @"}");

            // RecursiveCheck
            sb.Append(@"
" + ss + @"int " + c.Name + @"::RecursiveCheck(::xx::ObjManager& om) const {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    if (int r = this->BaseType::RecursiveCheck(om)) return r;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                // todo: 跳过不含有 Shared 的类型的生成
                sb.Append(@"
" + ss + @"    if (int r = om.RecursiveCheck(this->" + f.Name + ")) return r;");
            }
            sb.Append(@"
" + ss + @"    return 0;
" + ss + @"}");

            // RecursiveReset
            sb.Append(@"
" + ss + @"void " + c.Name + @"::RecursiveReset(::xx::ObjManager& om) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    this->BaseType::RecursiveReset(om);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
" + ss + @"    om.RecursiveReset(this->" + f.Name + ");");
            }
            sb.Append(@"
" + ss + @"}");

            // SetDefaultValue
            sb.Append(@"
" + ss + @"void " + c.Name + @"::SetDefaultValue(::xx::ObjManager& om) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
" + ss + @"    this->BaseType::SetDefaultValue(om);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;

                string dv = "";
                var v = f.GetValue(f.IsStatic ? null : o);
                dv = ft._GetDefaultValueDecl_Cpp(v);
                if (dv != "") {
                    dv = "this->" + f.Name + " = " + dv;
                }
                else {
                    dv = "om.SetDefaultValue(this->" + f.Name + ")";
                }

                sb.Append(@"
" + ss + @"    " + dv + @";");
            }
            sb.Append(@"
" + ss + @"}");

            // namespace }
            if (ss != "") {
                sb.Append(@"
}");
            }
        }
        sb.Append(@"
");
        sb._WriteToFile(Path.Combine(cfg.outdir_cpp, cfg.name + ".cpp"));
    }

    static void GenH_AJSON(this StringBuilder sb, Type c) {
        if (c._HasBaseType()) {
            GenH_AJSON(sb, c.BaseType);
        }
        var fs = c._GetFields();
        foreach (var f in fs) {
            sb.Append(", " + f.Name);
        }
    }

    public static void Gen_ajson_h() {
        var sb = new StringBuilder();
        sb.Append(@"#pragma once
#include """ + cfg.name + @".h""
#include ""ajson.hpp""");
        foreach (var c in cfg.localStructs) {
            if (c._HasClassMember()) continue;
            sb.Append(@"
AJSON(" + c._GetTypeDecl_Cpp());
            GenH_AJSON(sb, c);
            sb.Append(");");
        }
        sb.Append(@"
");
        sb._WriteToFile(Path.Combine(cfg.outdir_cpp, cfg.name + "_ajson.h"));
    }

    public static void Gen_empties() {
        var sb = new StringBuilder();
        foreach (var fn in createEmptyFiles) {
            var path = Path.Combine(cfg.outdir_cpp, fn);
            if (File.Exists(path)) {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("已跳过 " + fn);
                Console.ResetColor();
                continue;
            }
            sb._WriteToFile(path);
        }
    }
}
