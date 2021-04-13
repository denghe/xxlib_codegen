public static class GenLua {
    public static Cfg cfg;
    public static void Gen() {
        cfg = TypeHelpers.cfg;
        var sb = new System.Text.StringBuilder();

        // header( require, md5, register )

        if (cfg.refsCfgs.Count == 0) {
            sb.Append(@"
require('g_net')");
        }
        foreach (var c in cfg.refsCfgs) {
            sb.Append(@"
require('" + c.name + @"')");
        }

        sb.Append(@"
CodeGen_" + cfg.name + @"_md5 =""" + StringHelpers.MD5PlaceHolder + @"""
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
            if (c._HasBaseType()) {
                sb.Append(" -- : " + c.BaseType._GetTypeDecl_Lua());
            }
            if (c._IsClass()) {
                sb.Append(@"
    typeId = " + c._GetTypeId() + @",");
            }
            sb.Append(@"
    Create = function(o)
        if o == nil then
            o = {}
            setmetatable(o, " + cn + @")
        end");
            if (c._HasBaseType()) {
                sb.Append(@"
        " + c.BaseType._GetTypeDecl_Lua() + ".Create(o)");
            }

            var o = c._GetInstance();
            if (o == null) throw new System.Exception("o._GetInstance() == null. o.FullName = " + c.FullName);

            var fs = c._GetFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Lua(8) + @"
        o." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + " -- " + f.FieldType._GetTypeDesc_Lua());
            }
            sb.Append(@"
        return o
    end,
    Read = function(self, om)
        local d = om.d
        local r, n");
            if (fs.Exists(f => f.FieldType._IsList())) {
                sb.Append(", o");
            }
            if (c._HasCompatible()) {
                sb.Append(", len, e");
            }
            if (c._HasBaseType()) {
                var bt = c.BaseType._GetTypeDecl_Lua();
                sb.Append(@"
        -- base read
        r = " + bt + @".Read(self, om)
        if r ~= 0 then return r end");
            }
            string ss = "";
            if (c._HasCompatible()) {
                sb.Append(@"
        -- compatible handle
        r, len = d:Ru32()
        if r ~= 0 then return r end
        e = d:GetOffset() - 4 + len");
                ss = "    ";
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = @$"r, len = d:Rvu32()
{ss}        if len > d:GetLeft() then return -1 end
{ss}        o = {{}}
{ss}        self.{f.Name} = o
{ss}        for i = 1, len do
{ss}            {t._GetChildType()._GetReadCode_Lua("o[i]")}
{ss}            if r ~= 0 then return r end
{ss}        end";
                }
                else {
                    func = t._GetReadCode_Lua("self." + f.Name);
                }

                sb.Append(@"
        -- " + f.Name);

                if (c._HasCompatible()) {
                    sb.Append(@"
        if d:GetOffset() >= e then
            self." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + @"
        else");
                }

                if (string.IsNullOrEmpty(func))
                    throw new System.Exception("!!!");

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
        -- compatible handle
        if d:GetOffset() > e then return -1 end
        d:SetOffset(e)");
            }

            sb.Append(@"
        return 0
    end,
    Write = function(self, om)
        local d = om.d");
            if (fs.Exists(f => f.FieldType._IsList())) {
                sb.Append(@"
        local o, len");
            }
            if (c._HasBaseType()) {
                var bt = c.BaseType._GetTypeDecl_Lua();
                sb.Append(@"
        -- base read
        " + bt + @".Write(self, om)");
            }
            if (c._HasCompatible()) {
                sb.Append(@"
        -- compatible handle
        local bak = d:Wj(4)");
            }

            foreach (var f in fs) {
                var t = f.FieldType;
                string func;
                if (t._IsList()) {     // 当前设计方案仅支持 1 层嵌套
                    func = @$"o = self.{f.Name}
        len = #o
        d:Wvu32(len)
        for i = 1, len do
            {t._GetChildType()._GetWriteCode_Lua("o[i]")}
        end";
                }
                else {
                    func = t._GetWriteCode_Lua("self." + f.Name);
                }

                sb.Append(@"
        -- " + f.Name);

                if (string.IsNullOrEmpty(func))
                    throw new System.Exception("!!!");

                sb.Append(@"
        " + func);
            }

            if (c._HasCompatible()) {
                sb.Append(@"
        -- compatible handle
        d:Wu32_at(bak, d:GetLen() - bak);");
            }

            sb.Append(@"
    end
}
" + cn + @".__index = " + cn + @"
");
        }

        sb.Append(@"
local o = ObjMgr");
        foreach (var c in cfg.localClasss) {
            sb.Append(@"
o.Register(" + c._GetTypeDecl_Lua() + ")");
        }

        sb._WriteToFile(System.IO.Path.Combine(cfg.outdir_lua, cfg.name + ".lua"));
    }
}
