public static class GenJs {
    public static Cfg cfg;
    public static void Gen() {
        cfg = TypeHelpers.cfg;
        var sb = new System.Text.StringBuilder();

        // header( import, md5, register )

        foreach (var c in cfg.refsCfgs) {
            sb.Append(@"// <script language=""javascript"" src=""" + c.name + @".js""></script>
");
        }

        sb.Append(@"
var CodeGen_" + cfg.name + @"_md5 ='" + StringHelpers.MD5PlaceHolder + @"';
");
        // enums
        foreach (var e in cfg.enums) {
            var en = e._GetTypeDecl_Js();
            sb.Append(e._GetDesc()._GetComment_Js(0) + @"
class " + en + @" {");

            var fs = e._GetEnumFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Js(4) + @"
    static " + f.Name + " = " + f._GetEnumValue(e) + ";");
            }
            sb.Append(@"
}");
        }

        // class & structs
        foreach (var c in cfg.localClasssStructs) {
            var cn = c._GetTypeDecl_Js();
            sb.Append(c._GetDesc()._GetComment_Js(0) + @"
class " + cn + (c._IsClass() ? (@" extends "+(c._HasBaseType()? c.BaseType._GetTypeDecl_Js() : "ObjBase")) : "") + @" {");
            if (c._IsClass()) {
                sb.Append(@"
    static typeId = " + c._GetTypeId() + @";");
            }

            var o = c._GetInstance();
            if (o == null) throw new System.Exception("o._GetInstance() == null. o.FullName = " + c.FullName);

            var fs = c._GetFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Js(4) + @"
    " + f.Name + @" = " + f._GetDefaultValueDecl_Js(o) + "; // " + f.FieldType._GetTypeDesc_Js());
            }
            sb.Append(@"
    Read(d) {");
            if (c._HasBaseType()) {
                sb.Append(@"
        super.Read(d);");
            }
            if (c._HasCompatible()) {
                sb.Append(@"
        // compatible handle
        let e = d.offset - 4 + d.Ru32();");
            }

            if (fs.Exists(f => f.FieldType._IsList())) {
                sb.Append(@"
        let o, len;");
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = @$"len = d.Rvu();
        o = [];
        this.{f.Name} = o;
        for (let i = 0; i < len; ++i) {{";
                    if (t._GetChildType()._IsStruct()) {
                        func += @$"
            o[i] = new " + t._GetChildType()._GetTypeDecl_Js() + "();";
                    }
                    func += @$"
            {t._GetChildType()._GetReadCode_Js("o[i]", "")}
        }}";
                }
                else {
                    func = t._GetReadCode_Js(f.Name);
                }

                if (c._HasCompatible()) {
                    sb.Append(@"
        if (d.offset >= e) {
            this." + f.Name + @" = " + f._GetDefaultValueDecl_Js(o) + @"
        } else {");
                }

                if (string.IsNullOrEmpty(func))
                    throw new System.Exception("!!!");

                sb.Append(@$"
        " + func + @$"");

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
                    func = @$"o = this.{f.Name};
        len = o.length;
        d.Wvu(len);
        for (let i = 0; i < len; ++i) {{
            {t._GetChildType()._GetWriteCode_Js("o[i]", "")}
        }}";
                }
                else {
                    func = t._GetWriteCode_Js(f.Name);
                }

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
}");
            if (c._IsClass()) {
                sb.Append(@"
Data.Register(" + cn + @");");
            }
        }

        sb._WriteToFile(System.IO.Path.Combine(cfg.outdir_js, cfg.name + ".js"));
    }
}
