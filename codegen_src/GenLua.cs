public static class GenLua {
    public static Cfg cfg;
    public static void Gen() {
        cfg = TypeHelpers.cfg;
        var sb = new System.Text.StringBuilder();

        // header( require, md5, register )
        sb.Append(@"require ""objmgr""

CodeGen_" + cfg.name + @" = {
    md5 = """ + StringHelpers.MD5PlaceHolder + @""",
    Register = function()
        local o = ObjMgr");
        foreach (var c in cfg.localClasss) {
            sb.Append(@"
        o.Register(" + c._GetTypeDecl_Lua() + ")");
        }
        sb.Append(@"
    end
}
");
        // enums
        foreach (var e in cfg.enums) {
            var en = e._GetTypeDecl_Lua();
            sb.Append(e._GetDesc()._GetComment_Lua(0) + @"
" + en + @" = {");

            var fs = e._GetEnumFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Lua(4) + @"
    " + f.Name + " = " + f._GetEnumValue(e) + ",");
            }
            sb.Length--;
            sb.Append(@"
}");
        }

        // class & structs
        foreach (var c in cfg.localClasssStructs) {
            var cn = c._GetTypeDecl_Lua();
            sb.Append(c._GetDesc()._GetComment_Lua(0) + @"
" + cn + @" = {
    typeName = """ + cn + @""",");
            if (c._IsClass()) {
                sb.Append(@"
    typeId = " + c._GetTypeId() + @",");
            }
            sb.Append(@"
    Create = function(c)
        if c == nil then
            c = {}
            setmetatable(c, " + cn + @")
        end");
            if (c._HasBaseType()) {
                sb.Append(@"
        " + c.BaseType._GetTypeDecl_Lua() + ".Create(c)");
            }

            var o = c._GetInstance();
            if (o == null) throw new System.Exception("c._GetInstance() == null. c.FullName = " + c.FullName);

            var fs = c._GetFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Lua(8) + @"
        c." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + " -- " + f.FieldType._GetTypeDecl_Lua());
            }
            sb.Append(@"
        return o
    end,
    Read = function(self, om)
        local d = om.d, o, r");
            if (c._HasCompatible()) {
                sb.Append(", len, e");
            }
            if (c._HasBaseType()) {
                var bt = c.BaseType._GetTypeDecl_Lua();
                sb.Append(@"
        r = " + bt + @".Read(self, om)
        if r ~= 0 then
            return r
        end");
            }
            string ss = "";
            if (c._HasCompatible()) {
                sb.Append(@"
        r, len = d:Ru32()
        if r ~= 0 then return r end
        e = d:GetOffset() - 4 + len");
                ss = "    ";
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = t._GetChildType()._GetReadCode_Lua("o[i]");
                    func = @$"
{ss}        r, len = d:Rvu()
{ss}        if len > d:GetLeft() then return -1 end
{ss}        o = {{}}
{ss}        self." + f.Name + @$" = o
{ss}        for i = 1, len do
{ss}            " + func + @$"
{ss}            if r ~= 0 then return r end
{ss}        end";
                }
                else {
                    func = t._GetReadCode_Lua("self." + f.Name);
                }

                if (c._HasCompatible()) {
                    sb.Append(@"
        if d:GetOffset() >= eo then
            self." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + @"
        else");
                }

                sb.Append(@$"
{ss}        " + func + @$"
{ss}        if r ~= 0 then return r end");

                if (c._HasCompatible()) {
                    sb.Append(@"
        end");
                }
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        if d:GetOffset() > e then return -1 end
        d:SetOffset(e)");
            }

            sb.Append(@"
    end,
    Write = function(self, om)");

            // todo

            sb.Append(@"
    end
}
" + cn + @".__index = " + cn + @"
");
        }

        sb._WriteToFile(System.IO.Path.Combine(cfg.outdir_lua, cfg.name + ".lua"));
    }
}
