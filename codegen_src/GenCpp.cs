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

public static class GenCpp {


    // 单纯类型：写 Data 时全程不需要用到 ObjManager, 且没有打开兼容模式( 成员只能是简单类型，可嵌套结构体和泛型，不可出现 类 或 Shared/Weak )
    public static bool _IsPureType(this Type t, int level = 0, bool isFirst = true) {
        //if (t._IsExternal()) return t.IsValueType;
        if (isFirst) {
            TypeHelpers.tmpTypes.Clear();
        }
        TypeHelpers.tmpTypes.Add(t);

        if (t.IsEnum || t._IsNumeric() || t._IsString() || t._IsData()) return true;
        if (t.IsGenericType) {
            if (t._IsWeak() || t._IsShared()) return false;
            foreach (var ct in t.GetGenericArguments()) {
                if (TypeHelpers.tmpTypes.Contains(ct)) continue;
                if (!_IsPureType(ct, level + 1, false)) return false;
            }
            return true;
        }
        if (t._IsStruct() || t._IsClass()) {
            if (level > 0) return false;
            if (t._HasCompatible()) return false;
            var fs = t._GetExtractFields();
            foreach (var f in fs) {
                if (TypeHelpers.tmpTypes.Contains(f.FieldType)) continue;
                if (!_IsPureType(f.FieldType, level + 1, false)) return false;
            }
            return true;
        }
        throw new Exception("not impl? " + t.Name);
    }

    // 简化传参
    static Cfg cfg;
    static List<string> createEmptyFiles = new List<string>();

    public static void Gen() {
        cfg = TypeHelpers.cfg;
        createEmptyFiles.Clear();
        foreach (var c in cfg.typeInfos) {
            Console.WriteLine(c.Key + " is pure? " + c.Key._IsPureType());
        }

        Gen_h();
        Gen_cpp();
        Gen_ajson_h();
        Gen_empties();
    }

    public static void Gen_h() {
        var sb = new StringBuilder();
        sb.Append(@"/*CodeGen*/ #pragma once");

        // 包含依赖
        if (cfg.refsCfgs.Count == 0) {
            sb.Append(@"
/*CodeGen*/ #include <xx_obj.h>");
        }
        foreach (var c in cfg.refsCfgs) {
            sb.Append(@"
/*CodeGen*/ #include <" + c.name + @".h>");
        }

        // 前置切片
        createEmptyFiles.Add(cfg.name + ".h.inc");
        sb.Append(@"
/*CodeGen*/ #include <" + cfg.name + @".h.inc>");

        // 校验和注册
        sb.Append(@"
/*CodeGen*/ struct CodeGen_" + cfg.name + @" {
/*CodeGen*/     inline static const ::std::string md5 = """ + StringHelpers.MD5PlaceHolder + @""";
/*CodeGen*/     static void Register();
/*CodeGen*/     CodeGen_" + cfg.name + @"() { Register(); }
/*CodeGen*/ };
/*CodeGen*/ inline CodeGen_" + cfg.name + @" __CodeGen_" + cfg.name + @";");

        // 所有 本地 class 的预声明
        foreach (var c in cfg.localClasss) {
            var ns = c._GetNamespace_Cpp(false);
            if (string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ struct " + c.Name + ";");
            }
            else sb.Append(@"
/*CodeGen*/ namespace " + c._GetNamespace_Cpp(false) + @" { struct " + c.Name + "; }");
        }

        // 所有 本地 class 的 TypeId 映射
        if (cfg.localClasss.Count > 0) {
            sb.Append(@"
/*CodeGen*/ namespace xx {");
            foreach (var c in cfg.localClasss) {
                sb.Append(@"
/*CodeGen*/     template<> struct TypeId<" + c._GetTypeDecl_Cpp() + @"> { static const uint16_t value = " + c._GetTypeId() + @"; };");
            }
            sb.Append(@"
/*CodeGen*/ }
/*CodeGen*/ ");
        }

        // 所有 本地 enums
        foreach (var e in cfg.localEnums) {
            var ns = e._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ namespace " + ns + "{");
                ss = "    ";
            }

            sb.Append(e._GetDesc()._GetComment_Cpp(4, "/*CodeGen*/ ") + @"
/*CodeGen*/ " + ss + @"    enum class " + e.Name + @" : " + e._GetEnumUnderlyingTypeName_Cpp() + @" {");

            var fs = e._GetEnumFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Cpp(8 - ss.Length, "/*CodeGen*/ ") + @"
/*CodeGen*/ " + ss + @"        " + f.Name + " = " + f._GetEnumValue(e) + ",");
            }

            sb.Append(@"
/*CodeGen*/ " + ss + @"    };");

            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ }");
            }
        }

        var a = new Action<Type>((c) => {
            var o = c._GetInstance();
            var ns = c._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ namespace " + ns + @" {");
                ss = "    ";
            }

            // 头部
            var bt = c.BaseType;
            if (c._IsStruct()) {
                var btn = c._HasBaseType() ? (" : " + bt._GetTypeDecl_Cpp()) : "";
                sb.Append(c._GetDesc()._GetComment_Cpp(ss.Length, "/*CodeGen*/ ") + @"
/*CodeGen*/ " + ss + @"struct " + c.Name + btn + @" {
/*CodeGen*/ " + ss + @"    XX_OBJ_STRUCT_H(" + c.Name + @")");
            }
            else {
                var btn = c._HasBaseType() ? bt._GetTypeDecl_Cpp() : "::xx::ObjBase";
                sb.Append(c._GetDesc()._GetComment_Cpp(ss.Length, "/*CodeGen*/ ") + @"
/*CodeGen*/ " + ss + @"struct " + c.Name + " : " + btn + @" {
/*CodeGen*/ " + ss + @"    XX_OBJ_OBJECT_H(" + c.Name + @", " + btn + @")");
            }

            // 附加标签
            if (c._IsPureType()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    using IsSimpleType_v = " + c.Name + ";");
            }

            // 前置包含
            if (c._HasInclude()) {
                var fn = c._GetUnderlineFullname() + ".inc";
                createEmptyFiles.Add(fn);
                sb.Append(@"
/*CodeGen*/ #include <" + fn + @">");
            }

            // 成员
            foreach (var f in c._GetFieldsConsts()) {
                var ft = f.FieldType;
                var ftn = ft._GetTypeDecl_Cpp();
                sb.Append(f._GetDesc()._GetComment_Cpp(8, "/*CodeGen*/ ") + @"
/*CodeGen*/ " + ss + @"    " + (f.IsStatic ? "constexpr " : "") + ftn + " " + f.Name);

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
            if (c._IsClass() && c._IsPureType()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    static void WriteTo(xx::Data& d");
                foreach (var f in c._GetExtractFields()) {
                    var ft = f.FieldType;
                    var ftn = ft._GetTypeDecl_Cpp();
                    if (ft._IsString()) {
                        ftn = "std::string_view";
                    }
                    sb.Append(", " + ftn + " const& " + f.Name);
                }
                sb.Append(");");
            }

            // 后置包含
            if (c._HasInclude_()) {
                var fn = c._GetUnderlineFullname() + "_.inc";
                createEmptyFiles.Add(fn);
                sb.Append(@"
/*CodeGen*/ #include <" + fn + @">");
            }

            sb.Append(@"
/*CodeGen*/ " + ss + @"};");

            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ }");
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
/*CodeGen*/ namespace xx {");
            foreach (var c in cfg.localStructs) {
                sb.Append(@"
/*CodeGen*/ 	XX_OBJ_STRUCT_TEMPLATE_H(" + c._GetTypeDecl_Cpp() + @")");
                if (c._IsPureType()) {
                    sb.Append(@"
/*CodeGen*/     template<typename T> struct DataFuncs<T, std::enable_if_t<std::is_same_v<" + c._GetTypeDecl_Cpp() + @", std::decay_t<T>>>> {
/*CodeGen*/ 		template<bool needReserve = true>
/*CodeGen*/ 		static inline void Write(Data& d, T const& in) { (*(xx::ObjManager*)-1).Write(d, in); }
/*CodeGen*/ 		static inline int Read(Data_r& d, T& out) { return (*(xx::ObjManager*)-1).Read(d, out); }
/*CodeGen*/     };");
                }
            }
            sb.Append(@"
/*CodeGen*/ }");
        }

        // 后置切片
        {
            var fn = cfg.name + "_.h.inc";
            createEmptyFiles.Add(fn);
            sb.Append(@"
/*CodeGen*/ #include <" + fn + @">
");
        }

        sb._WriteToFile(Path.Combine(cfg.outdir_cpp, cfg.name + ".h"));
    }

    public static void Gen_cpp() {
        var sb = new StringBuilder();
        // 前置包含
        {
            var fn = cfg.name + ".cpp.inc";
            sb.Append("/*CodeGen*/ #include <" + cfg.name + @".h>
/*CodeGen*/ #include <" + fn + @">");
            createEmptyFiles.Add(fn);
        }

        // type id 注册
        sb.Append(@"
/*CodeGen*/ void CodeGen_" + cfg.name + @"::Register() {");
        foreach (var c in cfg.localClasss) {
            sb.Append(@"
/*CodeGen*/ 	::xx::ObjManager::Register<" + c._GetTypeDecl_Cpp() + @">();");
        }
        sb.Append(@"
/*CodeGen*/ }");

        // 模板适配
        if (cfg.localStructs.Count > 0) {
            sb.Append(@"
/*CodeGen*/ namespace xx {");
        }
        foreach (var c in cfg.localStructs) {
            var o = c._GetInstance();

            var ctn = c._GetTypeDecl_Cpp();
            var fs = c._GetFields();

            // Write
            sb.Append(@"
/*CodeGen*/ 	void ObjFuncs<" + ctn + @", void>::Write(::xx::ObjManager& om, ::xx::Data& d, " + ctn + @" const& in) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         ObjFuncs<" + btn + ">::Write(om, d, in);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/         auto bak = d.WriteJump(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType(1)) {
                    sb.Append(@"
/*CodeGen*/         d.Write(in." + f.Name + ");");
                }
                else {
                    sb.Append(@"
/*CodeGen*/         om.Write(d, in." + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/         d.WriteFixedAt(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
/*CodeGen*/     }");

            // WriteFast
            sb.Append(@"
/*CodeGen*/ 	void ObjFuncs<" + ctn + @", void>::WriteFast(::xx::ObjManager& om, ::xx::Data& d, " + ctn + @" const& in) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         ObjFuncs<" + btn + ">::Write<false>(om, d, in);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/         auto bak = d.WriteJump<false>(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType(1)) {
                    sb.Append(@"
/*CodeGen*/         d.Write<false>(in." + f.Name + ");");
                }
                else {
                    sb.Append(@"
/*CodeGen*/         om.Write<false>(d, in." + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/         d.WriteFixedAt<false>(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
/*CodeGen*/     }");

            // Read
            sb.Append(@"
/*CodeGen*/ 	int ObjFuncs<" + ctn + @", void>::Read(::xx::ObjManager& om, ::xx::Data_r& d, " + ctn + @"& out) {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();

                sb.Append(@"
/*CodeGen*/         if (int r = ObjFuncs<" + btn + ">::Read(om, d, out)) return r;");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/         uint32_t siz;
/*CodeGen*/         if (int r = d.ReadFixed(siz)) return r;
/*CodeGen*/         if (siz < sizeof(siz)) return __LINE__;
/*CodeGen*/         siz -= sizeof(siz);
/*CodeGen*/         if (siz > d.len - d.offset) return __LINE__;
/*CodeGen*/         xx::Data_r dr(d.buf + d.offset, siz);
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
/*CodeGen*/         if (dr.offset == siz) " + dv + @";
/*CodeGen*/         else if (int r = om.Read(dr, out." + f.Name + @")) return r;");
                }

                sb.Append(@"

/*CodeGen*/         d.offset += siz;");

            }
            else {
                foreach (var f in fs) {
                    if (f.FieldType._IsPureType(1)) {
                        sb.Append(@"
/*CodeGen*/         if (int r = d.Read(out." + f.Name + @")) return r;");
                    }
                    else {
                        sb.Append(@"
/*CodeGen*/         if (int r = om.Read(d, out." + f.Name + @")) return r;");
                    }
                }
            }

            sb.Append(@"
/*CodeGen*/         return 0;
/*CodeGen*/     }");

            // Append
            sb.Append(@"
/*CodeGen*/ 	void ObjFuncs<" + ctn + @", void>::Append(ObjManager &om, std::string& s, " + ctn + @" const& in) {
/*CodeGen*/ #ifndef XX_DISABLE_APPEND
/*CodeGen*/         s.push_back('{');
/*CodeGen*/         AppendCore(om, s, in);
/*CodeGen*/         s.push_back('}');
/*CodeGen*/ #endif
/*CodeGen*/     }
/*CodeGen*/ 	void ObjFuncs<" + ctn + @", void>::AppendCore(ObjManager &om, std::string& s, " + ctn + @" const& in) {
/*CodeGen*/ #ifndef XX_DISABLE_APPEND");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         auto sizeBak = s.size();
/*CodeGen*/         ObjFuncs<" + btn + ">::AppendCore(om, s, in);");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (f == fs[0]) {
                    if (c._HasBaseType()) {
                        sb.Append(@"
/*CodeGen*/         if (sizeBak < s.size()) {
/*CodeGen*/             s.push_back(',');
/*CodeGen*/         }");
                    }
                    sb.Append(@"
/*CodeGen*/         om.Append(s, ""\""" + f.Name + @"\"":"", in." + f.Name + @"); ");
                }
                else {
                    sb.Append(@"
/*CodeGen*/         om.Append(s, "",\""" + f.Name + @"\"":"", in." + f.Name + @");");
                }
            }
            sb.Append(@"
/*CodeGen*/ #endif
/*CodeGen*/     }");

            // Clone
            sb.Append(@"
/*CodeGen*/     void ObjFuncs<" + ctn + @">::Clone(::xx::ObjManager& om, " + ctn + @" const& in, " + ctn + @" &out) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         ObjFuncs<" + btn + ">::Clone_(om, in, out);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/         om.Clone_(in." + f.Name + ", out." + f.Name + ");");
            }
            sb.Append(@"
/*CodeGen*/     }");

            // RecursiveCheck
            sb.Append(@"
/*CodeGen*/     int ObjFuncs<" + ctn + @">::RecursiveCheck(::xx::ObjManager& om, " + ctn + @" const& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         if (int r = ObjFuncs<" + btn + ">::RecursiveCheck(om, in)) return r;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/         if (int r = om.RecursiveCheck(in." + f.Name + ")) return r;");
            }
            sb.Append(@"
/*CodeGen*/         return 0;
/*CodeGen*/     }");

            // RecursiveReset
            sb.Append(@"
/*CodeGen*/     void ObjFuncs<" + ctn + @">::RecursiveReset(::xx::ObjManager& om, " + ctn + @"& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         ObjFuncs<" + btn + ">::RecursiveReset(om, in);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/         om.RecursiveReset(in." + f.Name + ");");
            }
            sb.Append(@"
/*CodeGen*/     }");

            // SetDefaultValue
            sb.Append(@"
/*CodeGen*/     void ObjFuncs<" + ctn + @">::SetDefaultValue(::xx::ObjManager& om, " + ctn + @"& in) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                var btn = bt._GetTypeDecl_Cpp();
                sb.Append(@"
/*CodeGen*/         ObjFuncs<" + btn + ">::SetDefaultValue(om, in);");
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
/*CodeGen*/         " + dv + @";");
            }
            sb.Append(@"
/*CodeGen*/     }");
        }
        if (cfg.localStructs.Count > 0) {
            sb.Append(@"
/*CodeGen*/ }");
        }

        // class 成员函数
        foreach (var c in cfg.localClasss) {
            var ns = c._GetNamespace_Cpp(false);
            string ss = "";
            if (!string.IsNullOrEmpty(ns)) {
                sb.Append(@"
/*CodeGen*/ namespace " + ns + "{");
                ss = "    ";
            }

            var o = c._GetInstance();
            var fs = c._GetFields();

            // WriteTo
            if (c._IsPureType()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::WriteTo(xx::Data& d");
                foreach (var f in c._GetExtractFields()) {
                    var ft = f.FieldType;
                    var ftn = ft._GetTypeDecl_Cpp();
                    if (ft._IsString()) {
                        ftn = "std::string_view";
                    }
                    sb.Append(", " + ftn + " const& " + f.Name);
                }
                sb.Append(@") {
/*CodeGen*/ " + ss + @"    d.Write(xx::TypeId_v<" + c.Name + ">);");
                foreach (var f in c._GetExtractFields()) {
                    sb.Append(@"
/*CodeGen*/ " + ss + @"    d.Write(" + f.Name + ");");
                }
                sb.Append(@"
/*CodeGen*/ " + ss + @"}");
            }

            // Write
            sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::Write(::xx::ObjManager& om, ::xx::Data& d) const {");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    this->BaseType::Write(om, d);");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    auto bak = d.WriteJump(sizeof(uint32_t));");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                if (ft._IsPureType(1)) {
                    sb.Append(@"
/*CodeGen*/ " + ss + @"    d.Write(this->" + f.Name + ");");
                }
                else {
                    sb.Append(@"
/*CodeGen*/ " + ss + @"    om.Write(d, this->" + f.Name + ");");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    d.WriteFixedAt(bak, (uint32_t)(d.len - bak));");
            }

            sb.Append(@"
/*CodeGen*/ " + ss + @"}");

            // Read
            sb.Append(@"
/*CodeGen*/ " + ss + @"int " + c.Name + @"::Read(::xx::ObjManager& om, ::xx::Data_r& d) {");

            if (c._HasBaseType()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    if (int r = this->BaseType::Read(om, d)) return r;");
            }

            if (c._HasCompatible()) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    uint32_t siz;
/*CodeGen*/ " + ss + @"    if (int r = d.ReadFixed(siz)) return r;
/*CodeGen*/ " + ss + @"    if (siz < sizeof(siz)) return __LINE__;
/*CodeGen*/ " + ss + @"    siz -= sizeof(siz);
/*CodeGen*/ " + ss + @"    if (siz > d.len - d.offset) return __LINE__;
/*CodeGen*/ " + ss + @"    xx::Data_r dr(d.buf + d.offset, siz);
/*CodeGen*/ ");
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
/*CodeGen*/ " + ss + @"    if (dr.offset == siz) " + dv + @";
/*CodeGen*/ " + ss + @"    else if (int r = om.Read(dr, this->" + f.Name + @")) return r;");
                }

                sb.Append(@"

/*CodeGen*/ " + ss + @"    d.offset += siz;");
            }
            else {
                foreach (var f in fs) {
                    if (f.FieldType._IsPureType(1)) {
                        sb.Append(@"
/*CodeGen*/ " + ss + @"    if (int r = d.Read(this->" + f.Name + @")) return r;");
                    }
                    else {
                        sb.Append(@"
/*CodeGen*/ " + ss + @"    if (int r = om.Read(d, this->" + f.Name + @")) return r;");
                    }
                }
            }

            sb.Append(@"
/*CodeGen*/ " + ss + @"    return 0;
/*CodeGen*/ " + ss + @"}");

            // Append
            sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::Append(::xx::ObjManager& om, std::string& s) const {
/*CodeGen*/ #ifndef XX_DISABLE_APPEND
/*CodeGen*/ " + ss + @"    ::xx::Append(s, ""{\""__typeId__\"":" + c._GetTypeId() + @""");");
            sb.Append(@"
/*CodeGen*/ " + ss + @"    this->AppendCore(om, s);
/*CodeGen*/ " + ss + @"    s.push_back('}');
/*CodeGen*/ #endif
/*CodeGen*/ " + ss + @"}
/*CodeGen*/ " + ss + @"void " + c.Name + @"::AppendCore(::xx::ObjManager& om, std::string& s) const {
/*CodeGen*/ #ifndef XX_DISABLE_APPEND");

            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    this->BaseType::AppendCore(om, s);");
            }

            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    om.Append(s, "",\""" + f.Name + @"\"":"", this->" + f.Name + @");");
            }
            sb.Append(@"
/*CodeGen*/ #endif
/*CodeGen*/ " + ss + @"}");

            // Clone
            sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::Clone(::xx::ObjManager& om, void* const &tar) const {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    this->BaseType::Clone(om, tar);");
            }
            if (fs.Count > 0) {
                sb.Append(@"
/*CodeGen*/ " + ss + @"    auto out = (" + c._GetTypeDecl_Cpp() + @"*)tar;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    om.Clone_(this->" + f.Name + ", out->" + f.Name + ");");
            }
            sb.Append(@"
/*CodeGen*/ " + ss + @"}");

            // RecursiveCheck
            sb.Append(@"
/*CodeGen*/ " + ss + @"int " + c.Name + @"::RecursiveCheck(::xx::ObjManager& om) const {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    if (int r = this->BaseType::RecursiveCheck(om)) return r;");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                // todo: 跳过不含有 Shared 的类型的生成
                sb.Append(@"
/*CodeGen*/ " + ss + @"    if (int r = om.RecursiveCheck(this->" + f.Name + ")) return r;");
            }
            sb.Append(@"
/*CodeGen*/ " + ss + @"    return 0;
/*CodeGen*/ " + ss + @"}");

            // RecursiveReset
            sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::RecursiveReset(::xx::ObjManager& om) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    this->BaseType::RecursiveReset(om);");
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    om.RecursiveReset(this->" + f.Name + ");");
            }
            sb.Append(@"
/*CodeGen*/ " + ss + @"}");

            // SetDefaultValue
            sb.Append(@"
/*CodeGen*/ " + ss + @"void " + c.Name + @"::SetDefaultValue(::xx::ObjManager& om) {");
            if (c._HasBaseType()) {
                var bt = c.BaseType;
                sb.Append(@"
/*CodeGen*/ " + ss + @"    this->BaseType::SetDefaultValue(om);");
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
/*CodeGen*/ " + ss + @"    " + dv + @";");
            }
            sb.Append(@"
/*CodeGen*/ " + ss + @"}");

            // namespace }
            if (ss != "") {
                sb.Append(@"
/*CodeGen*/ }");
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
            sb.Append(@"
/*CodeGen*/ , " + f.Name);
        }
    }

    public static void Gen_ajson_h() {
        var sb = new StringBuilder();
        sb.Append(@"/*CodeGen*/ #pragma once
/*CodeGen*/ #include <" + cfg.name + @".h>
/*CodeGen*/ #include <ajson.hpp>");
        foreach (var c in cfg.localStructs) {
            if (c._HasClassMember()) continue;
            sb.Append(@"
/*CodeGen*/ AJSON(" + c._GetTypeDecl_Cpp());
            GenH_AJSON(sb, c);
            sb.Append(@"
/*CodeGen*/ );");
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
