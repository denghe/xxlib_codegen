public static class GenJs {
    public static Cfg cfg;
    public static void Gen() {
        cfg = TypeHelpers.cfg;
        var sb = new System.Text.StringBuilder();

        // header( import, md5, register )
        sb.Append("import { Data, ObjBase } from './xx_data.js';");

        foreach (var c in cfg.refsCfgs) {
            sb.Append(@"
import * from '" + c.name + @".js'");
        }

        sb.Append(@"
var CodeGen_" + cfg.name + @"_md5 ='" + StringHelpers.MD5PlaceHolder + @"'
");
        // enums
        foreach (var e in cfg.enums) {
            var en = e._GetTypeDecl_Lua();
            sb.Append(e._GetDesc()._GetComment_Cpp(0) + @"
class " + en + @" {");

            var fs = e._GetEnumFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Cpp(4) + @"
    static " + f.Name + " = " + f._GetEnumValue(e) + ";");
            }
            sb.Append(@"
}");
        }

        // class & structs
        foreach (var c in cfg.localClasssStructs) {
            var cn = c._GetTypeDecl_Lua();
            sb.Append(c._GetDesc()._GetComment_Cpp(0) + @"
class " + cn + @" extends "+(c._HasBaseType()? c.BaseType._GetTypeDecl_Lua() : "ObjBase") +@" {");
            if (c._IsClass()) {
                sb.Append(@"
    static typeId = " + c._GetTypeId() + @";");
            }

            var o = c._GetInstance();
            if (o == null) throw new System.Exception("o._GetInstance() == null. o.FullName = " + c.FullName);

            var fs = c._GetFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Lua(8) + @"
    " + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + "; // " + f.FieldType._GetTypeDesc_Lua());
            }
            sb.Append(@"
    Read(d) {");
            if (c._HasBaseType()) {
                sb.Append(@"
        super.Read(d);");
            }
            string ss = "";
            if (c._HasCompatible()) {
                sb.Append(@"
        // compatible handle
        let len = d.Ru32();
        let e = d.offset - 4 + len;");
                ss = "    ";
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = @$"let len = d.Rvu();
{ss}        if (len > d.GetLeft()) throw new Error(`len : ${{len}} > d.GetLeft() : ${{d.GetLeft()}}`);
{ss}        let o = [];
{ss}        this.{f.Name} = o;
{ss}        for (let i = 0; i < len; ++i) {{
{ss}            {t._GetChildType()._GetReadCode_Lua("o[i]")}
{ss}        }}";
                }
                else {
                    func = t._GetReadCode_Lua("this." + f.Name);
                }

                if (c._HasCompatible()) {
                    sb.Append(@"
        if (d.offset >= e) {
            self." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + @"
        } else {");
                }

                if (string.IsNullOrEmpty(func))
                    throw new System.Exception("!!!");

                sb.Append(@$"
{ss}        " + func + @$"");

                if (c._HasCompatible()) {
                    sb.Append(@"
        }");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        // compatible handle
        if (d.offset > e) throw new Error(`d.offset : ${{d.offset}} > e : ${{e}}`);
        d.offset = e;");
            }

            sb.Append(@"
    }
    Write(d) {");
            if (c._HasBaseType()) {
                sb.Append(@"
        // base read
        super.Write(d);");
            }
            if (c._HasCompatible()) {
                sb.Append(@"
        // compatible handle
        let bak = d.Wj(4);");
            }

            if (fs.Exists(f => f.FieldType._IsList())) {
                sb.Append(@"
        let o, len;");
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = @$"
        o = this.{f.Name};
        len = o.length;
        d.Wvu(len);
        for (let i = 0; i < len; ++i) {{
            {t._GetChildType()._GetWriteCode_Lua("o[i]")}
        }}";
                }
                else {
                    func = t._GetWriteCode_Lua("this." + f.Name);
                }

                sb.Append(@"
        // " + f.Name);

                if (string.IsNullOrEmpty(func))
                    throw new System.Exception("!!!");

                sb.Append(@"
        " + func);
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        // compatible handle
        d.Wu32at(bak, d.len - bak);");
            }

            sb.Append(@"
    }
}

Data.Register(" + cn + @");
");
        }

        sb._WriteToFile(System.IO.Path.Combine(cfg.outdir_js, cfg.name + ".js"));
    }
}
