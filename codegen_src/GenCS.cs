using System;
using System.IO;
using System.Text;

public static class GenCS {
    public static Cfg cfg;
    public static void Gen() {
        cfg = TypeHelpers.cfg;
        StringBuilder sb = new StringBuilder();
        sb.Gen_CSHead();
        sb.Gen_Content();
        sb._WriteToFile(Path.Combine(cfg.outdir_cs, cfg.name + ".cs"));
        sb.Clear();
    }



    public static void Gen_CSHead(this StringBuilder sb) {
        sb.Append(
$@"using System;
using System.Collections.Generic;
");
    }


    public static void Gen_Content(this StringBuilder sb) {

        for (int i = 0; i < cfg.localStructs.Count; i++)
        {
            GenStruct(sb, i);
        }

        for (int i = 0; i < cfg.localClasss.Count; i++)
        {
            GenLocalClass(sb, i);
        }



        var tb = new StringBuilder();

        foreach (var c in cfg.localClasss) {
            tb.Append($@"
         xx.ObjManager.Register<{c.FullName}>({c._GetTypeId()});");
        }

        sb.Append($@"

public static partial class CodeGen_{cfg.name}
{{
    public const string md5 = ""{ StringHelpers.MD5PlaceHolder }""; 
    public static void Register()
    {{{tb}
    }}
}}
");

    }



    private static void GenStruct(StringBuilder sb, int i)
    {
        var c = cfg.localStructs[i];
        var typeid = c._GetTypeId();

        var need_compatible = c._Has<TemplateLibrary.Compatible>();

        if (c.Namespace != null && (i == 0 || (i > 0 && cfg.localStructs[i - 1].Namespace != c.Namespace))) // namespace 去重
        {
            sb.Append($@"
namespace { c.Namespace  }
{{");
        }



        sb.Append(c._GetDesc()._GetComment_CSharp(4) +
$@"
    public partial class {c.Name}{c.BaseType._GetStructTypeBase_Csharp()}
    {{");

        var fs = c._GetFieldsConsts();
        foreach (var p in fs)
        {
            var pt = p.FieldType;
            var ptn = pt._GetTypeDecl_Csharp();

            if (pt._IsClass())
                throw new Exception($"need Shared<T> class:{c.Name} field:{p.Name}");


            if (pt._IsList() && pt.GetGenericArguments()[0]._IsClass())
                throw new Exception($"List<T> T not Shared<T> class:{c.Name} field:{p.Name}");

            if (p.IsStatic)
                throw new Exception($"static Field Not Supported class:{c.Name} field:{p.Name}");


            if (pt._IsStruct())
            {
                sb.Append(p._GetDesc()._GetComment_CSharp(8) +
$@"
        public {ptn} {p.Name} {{ get; set; }} = new {ptn}();
");
            }
            else
            {

                sb.Append(p._GetDesc()._GetComment_CSharp(8) +
$@"
        public {ptn} {p.Name} {{ get; set; }}
");

            }
        }
      

        #region Read
        sb.Append($@"
        public {(!c.IsValueType && c._HasBaseType() ? "new " : "")}int Read(xx.ObjManager om, xx.DataReader data)
        {{");

        if (!c.IsValueType && c._HasBaseType())
        {
            sb.Append($@"
            base.Read(om, data);
");
        }

        if (need_compatible || fs.Count > 0)
        {
            sb.Append(@"
            int err;");
        }

        if (need_compatible)
        {
            sb.Append($@"
            if ((err = data.ReadFiexd(out uint siz)) != 0) return err;
            int endoffset = (int)(data.Offset - sizeof(uint) + siz);
");
        }

        foreach (var f in fs)
        {

            if (!need_compatible)
            {
                if (f.FieldType._IsStruct())
                {
                  
                    sb.Append($@"
            if ((err = this.{f.Name}.Read(om, data)) != 0)
                return err;
");
                }
                else if (f.FieldType._IsNullable() && !f.FieldType._IsNullableNumber())
                {
                    sb.Append($@"
            if ((err = data.ReadFiexd(out byte have_{f.Name.ToLower()})) == 0)
            {{
                if (have_{f.Name.ToLower()} == 1)
                {{
                    this.{f.Name} = new {f.FieldType._GetTypeDecl_Csharp()}();
                    if ((err = this.{f.Name}.Read(om, data)) != 0)
                        return err;
                }}
            }}
            else return err;
");
                }
                else
                {
                    sb.Append($@"
            if ((err = om.{(f.FieldType._IsShared() ? "ReadObj" : (f.FieldType._IsListShared() ? "ReadObj" : "ReadFrom"))}(data, out {f.FieldType._GetTypeDecl_Csharp()} __{f.Name.ToLower()})) == 0)
            this.{f.Name} = __{f.Name.ToLower()};
            else return err;
");
                }
            }
            else
            {

                if (f.FieldType._IsStruct())
                {
                    sb.Append($@"
            if (data.Offset < endoffset && (err =  this.{f.Name}.Read(om, data)) != 0)
                return err;
");
                }
                else if (f.FieldType._IsNullable()&&!f.FieldType._IsNullableNumber())
                {
                    sb.Append($@"
            if (data.Offset < endoffset && (err = data.ReadFiexd(out byte have_{f.Name.ToLower()})) == 0)
            {{
                if (have_{f.Name.ToLower()} == 1)
                {{
                    this.{f.Name} = new {f.FieldType._GetTypeDecl_Csharp()}();
                    if ((err = this.{f.Name}.Read(om, data)) != 0)
                        return err;
                }}
            }}
            else
                return err;
");
                }
                else
                {
                    sb.Append($@"
            if (data.Offset >= endoffset)
                this.{f.Name} = default;
            else if ((err = om.{(f.FieldType._IsShared() ? "ReadObj" : (f.FieldType._IsListShared() ? "ReadObj" : "ReadFrom"))}(data, out {f.FieldType._GetTypeDecl_Csharp()} __{f.Name.ToLower()})) == 0)
                this.{f.Name} = __{f.Name.ToLower()};
            else return err;
");
                }
            }
        }
        if (need_compatible)
        {
            sb.Append($@"
            if (data.Offset > endoffset)
                throw new IndexOutOfRangeException($""struct: '{c.FullName}' offset error"");
            else
                data.Offset = endoffset;
            return 0;
        }}");
        }
        else
        {
            sb.Append($@"
            return 0;
        }}");
        }

        #endregion


        #region Write
        sb.Append($@"

        public {(!c.IsValueType && c._HasBaseType() ? "new " : "")}void Write(xx.ObjManager om, xx.Data data)
        {{");

        if (!c.IsValueType && c._HasBaseType())
        {
            sb.Append($@"
            base.Write(om, data);");
        }

        if (need_compatible)
        {
            sb.Append($@"
            var bak = data.Length;
            data.WriteFiexd(sizeof(uint));");
        }


        foreach (var f in fs)
        {
            if (f.FieldType._IsStruct())
            {
                sb.Append($@"
            this.{f.Name}.Write(om, data);");
            }
            else if (f.FieldType._IsNullable() && !f.FieldType._IsNullableNumber())
            {
                sb.Append($@"
            if (this.{f.Name} is null)
               data.WriteFiexd((byte)0);
            else
            {{
                data.WriteFiexd((byte)1);
                this.{f.Name}.Write(om, data);
            }}");
            }
            else
            {
                sb.Append($@"
            om.{(f.FieldType._IsShared() ? "WriteObj" : (f.FieldType._IsListShared() ? "WriteObj" : "WriteTo"))}(data, this.{f.Name});");
            }
        }

        if (need_compatible)
        {
            sb.Append(@"
            data.WriteFiexdAt(bak, (uint)(data.Length - bak));
        }     

    }");
        }
        else
        {
            sb.Append(@"
        }    

    }");
        }

        #endregion


        if (c.Namespace != null && ((i < cfg.localStructs.Count - 1 && cfg.localStructs[i + 1].Namespace != c.Namespace) || i == cfg.localStructs.Count - 1))
        {
            sb.Append(@"
}
");
        }
    }

    private static void GenLocalClass(StringBuilder sb, int i)
    {
        var c = cfg.localClasss[i];
        var typeid = c._GetTypeId();

        var need_compatible = c._Has<TemplateLibrary.Compatible>();

        if (c.Namespace != null && (i == 0 || (i > 0 && cfg.localClasss[i - 1].Namespace != c.Namespace))) // namespace 去重
        {
            sb.Append($@"
namespace { c.Namespace  }
{{");
        }



        sb.Append(c._GetDesc()._GetComment_CSharp(4) +
$@"
    public partial class {c.Name} : {c.BaseType._GetTypeBase_Csharp()}
    {{");

        var fs = c._GetFieldsConsts();
        foreach (var p in fs)
        {
            var pt = p.FieldType;
            var ptn = pt._GetTypeDecl_Csharp();

            if (pt._IsClass())
                throw new Exception($"need Shared<T> class:{c.Name} field:{p.Name}");


            if (pt._IsList() && pt.GetGenericArguments()[0]._IsClass())
                throw new Exception($"List<T> T not Shared<T> class:{c.Name} field:{p.Name}");

            if (p.IsStatic)
                throw new Exception($"static Field Not Supported class:{c.Name} field:{p.Name}");


            if (pt._IsStruct())
            {
                sb.Append(p._GetDesc()._GetComment_CSharp(8) +
$@"
        public {ptn} {p.Name} {{ get; set; }} = new {ptn}();
");
            }
            else
            {

                sb.Append(p._GetDesc()._GetComment_CSharp(8) +
$@"
        public {ptn} {p.Name} {{ get; set; }}
");

            }
        }

        sb.Append($@"
        public {(!c.IsValueType && c._HasBaseType() ? "new " : "")}ushort GetTypeid() => {typeid};
");

        #region Read
        sb.Append($@"
        public {(!c.IsValueType && c._HasBaseType() ? "new " : "")}int Read(xx.ObjManager om, xx.DataReader data)
        {{");

        if (!c.IsValueType && c._HasBaseType())
        {
            sb.Append($@"
            base.Read(om, data);
");
        }

        if (need_compatible || fs.Count > 0)
        {
            sb.Append(@"
            int err;");
        }

        if (need_compatible)
        {
            sb.Append($@"
            if ((err = data.ReadFiexd(out uint siz)) != 0) return err;
            int endoffset = (int)(data.Offset - sizeof(uint) + siz);
");
        }

        foreach (var f in fs)
        {

            if (!need_compatible)
            {
                if (f.FieldType._IsStruct())
                {

                    sb.Append($@"
            if ((err = this.{f.Name}.Read(om, data)) != 0)
                return err;
");
                }
                else if (f.FieldType._IsNullable() && !f.FieldType._IsNullableNumber())
                {
                    sb.Append($@"
            if ((err = data.ReadFiexd(out byte have_{f.Name.ToLower()})) == 0)
            {{
                if (have_{f.Name.ToLower()} == 1)
                {{
                    this.{f.Name} = new {f.FieldType._GetTypeDecl_Csharp()}();
                    if ((err = this.{f.Name}.Read(om, data)) != 0)
                        return err;
                }}
            }}
            else return err;
");
                }
                else
                {
                    sb.Append($@"
            if ((err = om.{(f.FieldType._IsShared() ? "ReadObj" : (f.FieldType._IsListShared() ? "ReadObj" : "ReadFrom"))}(data, out {f.FieldType._GetTypeDecl_Csharp()} __{f.Name.ToLower()})) == 0)
            this.{f.Name} = __{f.Name.ToLower()};
            else return err;
");
                }
            }
            else
            {
                if (f.FieldType._IsStruct())
                {
                    sb.Append($@"
            if (data.Offset < endoffset && (err =  this.{f.Name}.Read(om, data)) != 0)
                return err;
");
                }
                else if (f.FieldType._IsNullable() && !f.FieldType._IsNullableNumber())
                {
                    sb.Append($@"
            if (data.Offset < endoffset && (err = data.ReadFiexd(out byte have_{f.Name.ToLower()})) == 0)
            {{
                if (have_{f.Name.ToLower()} == 1)
                {{
                    this.{f.Name} = new {f.FieldType._GetTypeDecl_Csharp()}();
                    if ((err = this.{f.Name}.Read(om, data)) != 0)
                        return err;
                }}
            }}
            else
                return err;
");
                }
                else
                {
                    sb.Append($@"
            if (data.Offset >= endoffset)
                this.{f.Name} = default;
            else if ((err = om.{(f.FieldType._IsShared() ? "ReadObj" : (f.FieldType._IsListShared() ? "ReadObj" : "ReadFrom"))}(data, out {f.FieldType._GetTypeDecl_Csharp()} __{f.Name.ToLower()})) == 0)
                this.{f.Name} = __{f.Name.ToLower()};
            else return err;
");
                }
            }
        }
        if (need_compatible)
        {
            sb.Append($@"
            if (data.Offset > endoffset)
                throw new IndexOutOfRangeException($""typeid:{{ GetTypeid()}} class: '{c.FullName}' offset error"");
            else
                data.Offset = endoffset;
            return 0;
        }}");
        }
        else
        {
            sb.Append($@"
            return 0;
        }}");
        }

        #endregion


        #region Write
        sb.Append($@"

        public {(!c.IsValueType && c._HasBaseType() ? "new " : "")}void Write(xx.ObjManager om, xx.Data data)
        {{");

        if (!c.IsValueType && c._HasBaseType())
        {
            sb.Append($@"
            base.Write(om, data);");
        }

        if (need_compatible)
        {
            sb.Append($@"
            var bak = data.Length;
            data.WriteFiexd(sizeof(uint));");
        }


        foreach (var f in fs)
        {
            if (f.FieldType._IsStruct())
            {
                sb.Append($@"
            this.{f.Name}.Write(om, data);");
            }
            else if (f.FieldType._IsNullable() && !f.FieldType._IsNullableNumber())
            {
                sb.Append($@"
            if (this.{f.Name} is null)
               data.WriteFiexd((byte)0);
            else
            {{
                data.WriteFiexd((byte)1);
                this.{f.Name}.Write(om, data);
            }}");
            }
            else
            {
                sb.Append($@"
            om.{(f.FieldType._IsShared() ? "WriteObj" : (f.FieldType._IsListShared() ? "WriteObj" : "WriteTo"))}(data, this.{f.Name});");
            }
        }

        if (need_compatible)
        {
            sb.Append(@"
            data.WriteFiexdAt(bak, (uint)(data.Length - bak));
        }

        public override string ToString()            
           => xx.ObjManager.SerializeString(this);

    }");
        }
        else
        {
            sb.Append(@"
        }

        public override string ToString()            
           => xx.ObjManager.SerializeString(this);

    }");
        }

        #endregion


        if (c.Namespace != null && ((i < cfg.localClasss.Count - 1 && cfg.localClasss[i + 1].Namespace != c.Namespace) || i == cfg.localClasss.Count - 1))
        {
            sb.Append(@"
}
");
        }
    }
}
