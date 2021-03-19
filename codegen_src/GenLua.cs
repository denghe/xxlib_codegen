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
        local o = c or {}
        if c == nil then
            setmetatable(o, " + cn + @")
        end");

            var o = c._GetInstance();
            if (o == null) throw new System.Exception("c._GetInstance() == null. c.FullName = " + c.FullName);

            // 基类成员变量逐级展开
            var fs = c._GetExtractFields();
            foreach (var f in fs) {
                sb.Append(f._GetDesc()._GetComment_Lua(8) + @"
        o." + f.Name + @" = " + f._GetDefaultValueDecl_Lua(o) + " -- " + f.FieldType._GetTypeDecl_Lua());
            }
            sb.Append(@"
        return o
    end,
    Read = function(self, om)");
            if (c._HasBaseType()) {
                var bt = c.BaseType._GetTypeDecl_Lua();
                sb.Append(@"
        local p = getmetatable( o )
        p.__proto.FromBBuffer( bb, p )");
            }
            var ftns = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var f in fs) {
                var ft = f.FieldType;
                var ftn = "";
                if (ft._IsWeak() || ft._IsStruct()) {
                    throw new System.Exception("LUA does not support weak_ptr or struct");
                }
                if (ft._IsNullable()) {
                    ftn = "Nullable" + ft.GenericTypeArguments[0].Name;
                }
                else {
                    ftn = ft.IsEnum ? ft.GetEnumUnderlyingType().Name : ft._IsNumeric() ? ft.Name : "Object";
                    if (ft._IsData() || ft._IsString()) ftn = "Object";
                }
                if (ftns.ContainsKey(ftn)) ftns[ftn]++;
                else ftns.Add(ftn, 1);
            }
            foreach (var kvp in ftns) {
                if (kvp.Value > 1) {
                    sb.Append(@"
        local Read" + kvp.Key + @" = bb.Read" + kvp.Key);
                }
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                var ftn = "";
                if (ft._IsWeak() || ft._IsStruct()) {
                    throw new System.Exception("LUA does not support weak_ptr or struct");
                }
                if (ft._IsNullable()) {
                    ftn = "Nullable" + ft.GenericTypeArguments[0].Name;
                }
                else {
                    ftn = ft.IsEnum ? ft.GetEnumUnderlyingType().Name : ft._IsNumeric() ? ft.Name : "Object";
                    if (ft._IsData() || ft._IsString()) ftn = "Object";
                }
                if (ftns[ftn] > 1) {

                    sb.Append(@"
        o." + f.Name + @" = Read" + ftn + @"( bb )");
                }
                else {
                    sb.Append(@"
        o." + f.Name + @" = bb:Read" + ftn + @"()");
                }
            }
            if (c._IsList()) {
                var fn = "ReadObject";
                var ct = c.GenericTypeArguments[0];
                if (ct._IsWeak() || ct._IsStruct()) {
                    throw new System.Exception("LUA does not support weak_ptr or struct");
                }
                if (!ct._IsClass() && !ct._IsData() && !ct._IsString()) {
                    if (ct.IsEnum) {
                        var ctn = ct.GetEnumUnderlyingType().Name;
                        fn = "Read" + ctn;
                    }
                    else {
                        if (ct._IsNullable()) {
                            fn = "ReadNullable" + ct.GenericTypeArguments[0].Name;
                        }
                        else {
                            fn = "Read" + ct.Name;
                        }
                    }
                }
                sb.Append(@"
		local len = bb:ReadUInt32()
        local f = BBuffer." + fn + @"
		for i = 1, len do
			o[ i ] = f( bb )
		end");
            }
            sb.Append(@"
    end,
    Write = function(self, om)");
            if (c._HasBaseType()) {
                var bt = c.BaseType._GetTypeDecl_Lua();
                sb.Append(@"
        local p = getmetatable( o )
        p.__proto.ToBBuffer( bb, p )");
            }
            foreach (var kvp in ftns) {
                if (kvp.Value > 1) {
                    sb.Append(@"
        local Write" + kvp.Key + @" = bb.Write" + kvp.Key);
                }
            }
            foreach (var f in fs) {
                var ft = f.FieldType;
                var ftn = "";
                if (ft._IsWeak() || ft._IsStruct()) {
                    throw new System.Exception("LUA does not support weak_ptr or struct");
                }
                if (ft._IsNullable()) {
                    ftn = "Nullable" + ft.GenericTypeArguments[0].Name;
                }
                else {
                    ftn = ft.IsEnum ? ft.GetEnumUnderlyingType().Name : ft._IsNumeric() ? ft.Name : "Object";
                    if (ft._IsData() || ft._IsString()) ftn = "Object";
                }
                if (ftns[ftn] > 1) {
                    sb.Append(@"
        Write" + ftn + @"( bb, o." + f.Name + @" )");
                }
                else {
                    sb.Append(@"
        bb:Write" + ftn + @"( o." + f.Name + @" )");
                }
            }
            if (c._IsList()) {
                var fn = "WriteObject";
                var ct = c.GenericTypeArguments[0];
                if (!ct._IsClass() && !ct._IsData() && !ct._IsString()) {
                    if (ct.IsEnum) {
                        var ctn = ct.GetEnumUnderlyingType().Name;
                        fn = "Write" + ctn;
                    }
                    else {
                        var ctn = ct.Name;
                        fn = "Write" + ctn;
                    }

                }
                sb.Append(@"
        local len = #o
		bb:WriteUInt32( len )
        local f = BBuffer." + fn + @"
        for i = 1, len do
			f( bb, o[ i ]" + @" )
		end");
            }
            sb.Append(@"
    end
}
BBuffer.Register( " + cn + @" )");
        }

        // 临时方案
        sb.Replace("`1", "");

        sb._WriteToFile(System.IO.Path.Combine(cfg.outdir_lua, cfg.name + ".lua"));
    }
}
