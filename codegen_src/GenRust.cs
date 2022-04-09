using System;
using System.IO;
using System.Text;


public static class GenRust
{
    public static Cfg cfg;
    public static void Gen()
    {
        cfg = TypeHelpers.cfg;


        StringBuilder sb = new StringBuilder();
        sb.Gen_Rust_Head();
        sb.Gen_Rust_Content();      
        sb._WriteToFile(Path.Combine(cfg.outdir_rs, cfg.name.ToLower() + ".rs"));
        sb.Clear();
    }


    public static string checkname(string name)
    {
        switch (name)
        {
            case "type":
                return "type_";
            case "self":
                return "self_";
            case "use":
                return "use_";
            case "loop":
                return "loop_";
            case "mod":
                return "mod_";
            case "pub":
                return "pub_";
            case "Self":
                return "Self_";
            case "super":
                return "super_";
            default:
                return name;
        }
    }

    public static void Gen_Rust_Head(this StringBuilder sb)
    {
        var tb = new StringBuilder();

        foreach (var c in cfg.localClasss)
        {
            tb.Append($@"
    ObjectManager::register::<{c._GetTypeName_Rust()}>(stringify!({c._GetTypeName_Rust()}));");
        }

        var refs = new StringBuilder();
        foreach (var item in cfg.refsCfgs)
        {
            refs.Append(@$"
use super::{item.name.ToLower()}::*;");
        } 

        sb.Append(
$@"use xxlib::*;
use xxlib_builder::*;{refs}

#[allow(dead_code)]
const  MD5:&str=""{StringHelpers.MD5PlaceHolder}"";

#[allow(dead_code,non_snake_case)]
pub fn CodeGen_{cfg.name}(){{{tb}
}}");
    }

    public static void Gen_Rust_Content(this StringBuilder sb)
    {
        foreach (var e in cfg.localClasss)
        {
            make_class(e, sb);
        }

        foreach (var e in cfg.localStructs)
        {
            make_struct(e, sb);
        }

        foreach (var e in cfg.localEnums)
        {
            make_enum(e,sb);
        }
    }

    public static void make_class(Type e, StringBuilder sb)
    {
        var name = e._GetTypeName_Rust();
        var typeid = e._GetTypeId();
        var o = e._GetInstance();
        var cp = e._HasCompatible() ? "true" : "false";
        var base_type = e.BaseType._GetStructTypeBase_Rust();

        sb.Append($@"
{e._GetDesc()._GetComment_Rust(0)}
#[allow(unreachable_patterns,unused_imports,dead_code,non_snake_case,non_camel_case_types)]
pub const {name.ToUpper()}_TYPE_ID:u16 = {typeid}u16;
{e._GetDesc()._GetComment_Rust(0)}
#[allow(unused_imports,dead_code,non_snake_case,non_camel_case_types)]
#[derive(build,Debug)]
#[cmd(typeid({typeid}),compatible({cp}))]
pub struct {name}{{");

        if (base_type != "")
        {
            sb.Append($@"
    /// Parent Class
    pub base:{base_type},");
        }

        var fs = e._GetFieldsConsts();
        foreach (var f in fs)
        {
            var ft = f.FieldType;
            var ftn = ft._GetTypeDecl_Rust();

            if (f.IsStatic)
                throw new Exception($"struct field not static:{e.FullName}->{f.Name}");


            sb.Append($@"{f._GetDesc()._GetComment_Rust(4)}");
            var v = f.GetValue(o);
            var dv = ft._GetDefaultValueDecl_Rust(v);
            if (dv != "")
                sb.Append($@"
    #[cmd(default({dv}))]");

            sb.Append($@"
    pub {checkname(f.Name)}:{ftn},");

        }

        sb.Append(@"
}");

    }

    public static void make_struct(Type e, StringBuilder sb)
    {
        var name = e._GetTypeName_Rust();
        var o = e._GetInstance();
        var cp = e._HasCompatible() ? "true" : "false";
        var base_type = e.BaseType._GetStructTypeBase_Rust();

        sb.Append($@"
{e._GetDesc()._GetComment_Rust(0)}
#[allow(unused_imports,dead_code,non_snake_case,non_camel_case_types)]
#[derive(build,Debug)]
#[cmd(compatible({cp}))]
pub struct {name}{{");

        if (base_type != "")
        {
            sb.Append($@"
    /// Parent Class
    pub base:{base_type},");
        }

        var fs = e._GetFieldsConsts();
        foreach (var f in fs)
        {
            var ft = f.FieldType;
            var ftn = ft._GetTypeDecl_Rust();

            if (f.IsStatic)
                throw new Exception($"struct field not static:{e.FullName}->{f.Name}");


            sb.Append($@"{f._GetDesc()._GetComment_Rust(4)}");
            var v = f.GetValue(o);
            var dv = ft._GetDefaultValueDecl_Rust(v);
            if (dv != "")
                sb.Append($@"
    #[cmd(default({dv}))]");

            sb.Append($@"
    pub {checkname(f.Name)}:{ftn},");

        }

        sb.Append(@"
}");

    }

    public static void make_enum(Type e, StringBuilder sb)
    {
        var name = e._GetTypeName_Rust();
        var type = e._GetEnumUnderlyingTypeName_Rust();

       
        sb.Append($@"
{e._GetDesc()._GetComment_Rust(0)}
#[allow(unused_imports,dead_code,non_snake_case,non_camel_case_types)]
#[build_enum({type})]
pub enum {name}{{");      

        var fs = e._GetEnumFields();
        foreach (var f in fs)
        {
            sb.Append($@"{f._GetDesc()._GetComment_Rust(4)}
    {checkname(f.Name)} = " + f._GetEnumValue(e) + ",");
        }
        sb.Append(@"
}");
    }

}
